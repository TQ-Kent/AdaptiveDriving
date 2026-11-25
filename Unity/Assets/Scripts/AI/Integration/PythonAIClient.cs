using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using F1Racing.AI.Driving;
using UnityEngine;

namespace F1Racing.AI.Integration
{
    /// <summary>
    /// Client for the local Python AI service (Python/ai_service/server.py): one JSON line out, one back.
    /// Call only at permitted boundaries (between sessions, a player-lap boundary), never from physics code.
    /// Any timeout, refused connection or invalid reply returns null, and the caller keeps the last approved setting.
    /// Replies are proposals only: they must go through the FairnessGovernor and DifficultyConfigApplier.
    /// </summary>
    public static class PythonAIClient
    {
        public const int SchemaVersion = 1;
        public const string Host = "127.0.0.1";
        public const int Port = 47800;

        [Serializable]
        public class Request
        {
            public int schema = SchemaVersion;
            public string type;               // "ping" | "recommend_difficulty"
            public float current;             // current approved session difficulty
            public LapObservation[] laps = Array.Empty<LapObservation>();
        }

        [Serializable]
        public class Reply
        {
            public bool ok;
            public int schema;
            public string error;
            public string service;
            public string version;
            public float difficulty;
            public float uncertainty;
            public string model_version;
            public bool supported;
        }

        public static async Task<Reply> Send(Request request, int timeoutMs = 250)
        {
            string json = JsonUtility.ToJson(request);
            var client = new TcpClient();
            try
            {
                Task<string> io = Exchange(client, json);
                if (await Task.WhenAny(io, Task.Delay(timeoutMs)) != io)
                {
                    _ = io.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe the abort
                    return null;
                }
                Reply reply = JsonUtility.FromJson<Reply>(await io);
                return IsValid(request.type, reply) ? reply : null;
            }
            catch (Exception) // refused connection, dropped socket, malformed JSON
            {
                return null;
            }
            finally
            {
                client.Dispose();
            }
        }

        /// <summary>Rejects error replies, schema mismatches and out-of-domain values.</summary>
        public static bool IsValid(string requestType, Reply r)
        {
            if (r == null || !r.ok || r.schema != SchemaVersion) return false;
            if (requestType != "recommend_difficulty") return true;
            return float.IsFinite(r.difficulty) && r.difficulty >= 0f && r.difficulty <= 1f
                && float.IsFinite(r.uncertainty) && r.uncertainty >= 0f
                && !string.IsNullOrEmpty(r.model_version);
        }

        // Runs on the thread pool: no Unity API in here.
        private static async Task<string> Exchange(TcpClient client, string json)
        {
            await client.ConnectAsync(Host, Port).ConfigureAwait(false);
            NetworkStream stream = client.GetStream();
            byte[] data = Encoding.UTF8.GetBytes(json + "\n");
            await stream.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await reader.ReadLineAsync().ConfigureAwait(false);
        }
    }
}

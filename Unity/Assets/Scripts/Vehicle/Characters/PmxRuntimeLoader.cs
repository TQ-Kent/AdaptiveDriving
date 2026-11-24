using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace F1Racing.Vehicle.Characters
{
    /// <summary>
    /// Minimal runtime loader for MikuMikuDance PMX 2.x models.
    /// Builds a SkinnedMeshRenderer with the model's bone hierarchy (no IK, morphs or physics).
    /// The model is built in its own space: Y up, facing -Z, 1 unit = 1 MMD unit.
    /// </summary>
    public sealed class PmxModel
    {
        public GameObject Root;
        public Transform[] Bones;
        public readonly Dictionary<string, Transform> BonesByName = new Dictionary<string, Transform>();
        public float Height;
        public float MinY;
        public Transform FaceAnchor; // child of the head bone at the centre of the head mesh
        public Transform EyeAnchor;  // child of the head bone between the eyes (eye material centre)
        public Renderer Renderer;

        public Transform Bone(string name) => BonesByName.TryGetValue(name, out Transform t) ? t : null;
    }

    public static class PmxRuntimeLoader
    {
        public static PmxModel Load(byte[] data, string name, string resourcesTexFolder)
        {
            var r = new BinaryReader(new MemoryStream(data));
            if (Encoding.ASCII.GetString(r.ReadBytes(4)) != "PMX ")
                throw new InvalidDataException("Not a PMX file");
            r.ReadSingle(); // version
            byte[] g = r.ReadBytes(r.ReadByte());
            Encoding enc = g[0] == 0 ? Encoding.Unicode : Encoding.UTF8;
            int addUv = g[1], vSize = g[2], tSize = g[3], bSize = g[5];

            string Text() => enc.GetString(r.ReadBytes(r.ReadInt32()));
            int Idx(int size) => size == 1 ? r.ReadSByte() : size == 2 ? r.ReadInt16() : r.ReadInt32();
            int VIdx() => vSize == 1 ? r.ReadByte() : vSize == 2 ? r.ReadUInt16() : r.ReadInt32();
            Vector3 V3() => new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            for (int i = 0; i < 4; i++) Text(); // names + comments

            // ---- Vertices ----
            int nv = r.ReadInt32();
            var pos = new Vector3[nv];
            var nrm = new Vector3[nv];
            var uv = new Vector2[nv];
            var bw = new BoneWeight[nv];
            for (int i = 0; i < nv; i++)
            {
                pos[i] = V3();
                nrm[i] = V3();
                uv[i] = new Vector2(r.ReadSingle(), 1f - r.ReadSingle());
                r.ReadBytes(16 * addUv);
                byte type = r.ReadByte();
                var w = new BoneWeight();
                switch (type)
                {
                    case 0: // BDEF1
                        w.boneIndex0 = Mathf.Max(0, Idx(bSize)); w.weight0 = 1f; break;
                    case 1: // BDEF2
                    case 3: // SDEF (treated as BDEF2)
                        w.boneIndex0 = Mathf.Max(0, Idx(bSize));
                        w.boneIndex1 = Mathf.Max(0, Idx(bSize));
                        w.weight0 = r.ReadSingle(); w.weight1 = 1f - w.weight0;
                        if (type == 3) r.ReadBytes(36);
                        break;
                    default: // BDEF4 / QDEF
                        w.boneIndex0 = Mathf.Max(0, Idx(bSize)); w.boneIndex1 = Mathf.Max(0, Idx(bSize));
                        w.boneIndex2 = Mathf.Max(0, Idx(bSize)); w.boneIndex3 = Mathf.Max(0, Idx(bSize));
                        w.weight0 = r.ReadSingle(); w.weight1 = r.ReadSingle();
                        w.weight2 = r.ReadSingle(); w.weight3 = r.ReadSingle();
                        float sum = w.weight0 + w.weight1 + w.weight2 + w.weight3;
                        if (sum > 1e-5f) { w.weight0 /= sum; w.weight1 /= sum; w.weight2 /= sum; w.weight3 /= sum; }
                        else { w.weight0 = 1f; }
                        break;
                }
                bw[i] = w;
                r.ReadSingle(); // edge scale
            }

            // ---- Faces ----
            int nf = r.ReadInt32();
            var faces = new int[nf];
            for (int i = 0; i < nf; i++) faces[i] = VIdx();

            // ---- Textures ----
            int nt = r.ReadInt32();
            var texNames = new string[nt];
            for (int i = 0; i < nt; i++)
                texNames[i] = Path.GetFileNameWithoutExtension(Text().Replace('\\', '/'));

            // ---- Materials ----
            int nm = r.ReadInt32();
            var matTex = new string[nm];
            var matCount = new int[nm];
            var matAlpha = new float[nm];
            for (int i = 0; i < nm; i++)
            {
                Text(); Text();
                Vector4 diffuse = new Vector4(r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                r.ReadBytes(12 + 4 + 12); // specular, power, ambient
                r.ReadByte();             // flags
                r.ReadBytes(16 + 4);      // edge colour + size
                int ti = Idx(tSize);
                Idx(tSize); r.ReadByte(); // sphere tex + mode
                if (r.ReadByte() == 0) Idx(tSize); else r.ReadByte(); // toon
                Text();
                matCount[i] = r.ReadInt32();
                matTex[i] = ti >= 0 && ti < nt ? texNames[ti] : null;
                matAlpha[i] = diffuse.w;
            }

            // ---- Bones ----
            int nb = r.ReadInt32();
            var boneNames = new string[nb];
            var bonePos = new Vector3[nb];
            var boneParent = new int[nb];
            for (int i = 0; i < nb; i++)
            {
                boneNames[i] = Text(); Text();
                bonePos[i] = V3();
                boneParent[i] = Idx(bSize);
                r.ReadInt32(); // layer
                ushort fl = r.ReadUInt16();
                if ((fl & 0x1) != 0) Idx(bSize); else V3();
                if ((fl & 0x300) != 0) { Idx(bSize); r.ReadSingle(); }
                if ((fl & 0x400) != 0) V3();
                if ((fl & 0x800) != 0) { V3(); V3(); }
                if ((fl & 0x2000) != 0) r.ReadInt32();
                if ((fl & 0x20) != 0)
                {
                    Idx(bSize); r.ReadInt32(); r.ReadSingle();
                    int links = r.ReadInt32();
                    for (int k = 0; k < links; k++)
                    {
                        Idx(bSize);
                        if (r.ReadByte() != 0) { V3(); V3(); }
                    }
                }
            }

            // ---- Build hierarchy (root at identity) ----
            var model = new PmxModel();
            var root = new GameObject(name);
            model.Root = root;
            var armature = new GameObject("Armature").transform;
            armature.SetParent(root.transform, false);

            var bones = new Transform[nb];
            for (int i = 0; i < nb; i++)
            {
                bones[i] = new GameObject(boneNames[i]).transform;
                bones[i].SetParent(armature, false);
                bones[i].localPosition = bonePos[i];
                if (!model.BonesByName.ContainsKey(boneNames[i])) model.BonesByName.Add(boneNames[i], bones[i]);
            }
            for (int i = 0; i < nb; i++)
            {
                int p = boneParent[i];
                if (p >= 0 && p < nb && p != i) bones[i].SetParent(bones[p], true);
            }
            model.Bones = bones;

            var bind = new Matrix4x4[nb];
            for (int i = 0; i < nb; i++) bind[i] = bones[i].worldToLocalMatrix * root.transform.localToWorldMatrix;

            // ---- Mesh ----
            var mesh = new Mesh { name = name + "_Mesh" };
            if (nv > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.uv = uv;
            mesh.boneWeights = bw;
            mesh.bindposes = bind;
            mesh.subMeshCount = nm;
            int offset = 0;
            for (int i = 0; i < nm; i++)
            {
                var tris = new int[matAlpha[i] > 0.001f ? matCount[i] : 0];
                if (tris.Length > 0) System.Array.Copy(faces, offset, tris, 0, tris.Length);
                mesh.SetTriangles(tris, i);
                offset += matCount[i];
            }
            mesh.RecalculateBounds();

            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mats = new Material[nm];
            for (int i = 0; i < nm; i++) mats[i] = BuildMaterial(lit, matTex[i], resourcesTexFolder);

            var meshGo = new GameObject("Body");
            meshGo.transform.SetParent(root.transform, false);
            var smr = meshGo.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterials = mats;
            smr.updateWhenOffscreen = true;
            smr.quality = SkinQuality.Bone4;

            float minY = float.MaxValue, maxY = float.MinValue;
            foreach (Vector3 v in pos) { minY = Mathf.Min(minY, v.y); maxY = Mathf.Max(maxY, v.y); }
            model.MinY = minY;
            model.Height = maxY - minY;

            // Face anchor: centre of vertices driven mainly by the head bone.
            Transform head = model.Bone("頭");
            if (head != null)
            {
                int hi = System.Array.IndexOf(bones, head);
                Vector3 sum = Vector3.zero; int count = 0;
                for (int i = 0; i < nv; i++)
                    if (bw[i].boneIndex0 == hi && bw[i].weight0 > 0.5f) { sum += pos[i]; count++; }
                var anchor = new GameObject("FaceAnchor").transform;
                anchor.SetParent(head, false);
                anchor.position = count > 0 ? sum / count : head.position;
                model.FaceAnchor = anchor;

                // Eye anchor: centre of the eye material's triangles (texture name contains "eye").
                Vector3 eyeSum = Vector3.zero; int eyeCount = 0; int off = 0;
                for (int m = 0; m < nm; m++)
                {
                    if (matTex[m] != null && matTex[m].ToLowerInvariant().Contains("_eye"))
                        for (int k = off; k < off + matCount[m]; k++) { eyeSum += pos[faces[k]]; eyeCount++; }
                    off += matCount[m];
                }
                var eye = new GameObject("EyeAnchor").transform;
                eye.SetParent(head, false);
                eye.position = eyeCount > 0 ? eyeSum / eyeCount : anchor.position;
                model.EyeAnchor = eye;
            }
            model.Renderer = smr;
            return model;
        }

        private static Material BuildMaterial(Shader shader, string texName, string folder)
        {
            Texture2D tex = string.IsNullOrEmpty(texName) ? null : Resources.Load<Texture2D>(folder + texName);
            var m = new Material(shader) { name = "PMX_" + (texName ?? "untextured") };
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.mainTexture = tex; }
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.2f);
            m.SetFloat("_Metallic", 0f);
            m.SetFloat("_Cull", 0f); // double-sided

            if (texName != null && texName.Contains("cheek"))
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                m.SetFloat("_ZWrite", 0f);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                m.SetFloat("_AlphaClip", 1f);
                m.SetFloat("_Cutoff", 0.5f);
                m.EnableKeyword("_ALPHATEST_ON");
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            return m;
        }
    }
}

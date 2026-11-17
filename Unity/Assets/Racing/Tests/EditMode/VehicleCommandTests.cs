using NUnit.Framework;
using Racing.Contracts;

namespace Racing.Tests.EditMode
{
    public class VehicleCommandTests
    {
        [Test]
        public void ClampsOutOfRangeInputs()
        {
            var c = new VehicleCommand(2f, -1f, 5f);
            Assert.AreEqual(1f, c.Steering);
            Assert.AreEqual(0f, c.Throttle);
            Assert.AreEqual(1f, c.Brake);
        }

        [Test]
        public void RejectsNonFiniteInputs()
        {
            var c = new VehicleCommand(float.NaN, float.PositiveInfinity, float.NegativeInfinity);
            Assert.AreEqual(0f, c.Steering);
            Assert.AreEqual(0f, c.Throttle);
            Assert.AreEqual(0f, c.Brake);
        }
    }
}

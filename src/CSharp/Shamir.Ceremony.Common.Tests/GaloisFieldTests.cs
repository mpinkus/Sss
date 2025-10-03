using Shamir.Ceremony.Common.Cryptography;
using System.Reflection;

namespace Shamir.Ceremony.Common.Tests
{
    [TestClass]
    public sealed class GaloisFieldTests
    {
        private static MethodInfo? _gf256AddMethod;
        private static MethodInfo? _gf256MultiplyMethod;
        private static MethodInfo? _gf256DivideMethod;
        private static MethodInfo? _gf256InverseMethod;

        [ClassInitialize]
        public static void ClassSetup(TestContext context)
        {
            var type = typeof(ShamirSecretShare);
            _gf256AddMethod = type.GetMethod("GF256Add", BindingFlags.NonPublic | BindingFlags.Static);
            _gf256MultiplyMethod = type.GetMethod("GF256Multiply", BindingFlags.NonPublic | BindingFlags.Static);
            _gf256DivideMethod = type.GetMethod("GF256Divide", BindingFlags.NonPublic | BindingFlags.Static);
            _gf256InverseMethod = type.GetMethod("GF256Inverse", BindingFlags.NonPublic | BindingFlags.Static);
        }

        private static byte GF256Add(byte a, byte b)
        {
            return (byte)_gf256AddMethod!.Invoke(null, new object[] { a, b })!;
        }

        private static byte GF256Multiply(byte a, byte b)
        {
            return (byte)_gf256MultiplyMethod!.Invoke(null, new object[] { a, b })!;
        }

        private static byte GF256Divide(byte a, byte b)
        {
            return (byte)_gf256DivideMethod!.Invoke(null, new object[] { a, b })!;
        }

        private static byte GF256Inverse(byte a)
        {
            return (byte)_gf256InverseMethod!.Invoke(null, new object[] { a })!;
        }

        [TestMethod]
        public void GF256Add_CommutativeProperty()
        {
            var testCases = new (byte, byte)[]
            {
                (0, 0), (1, 1), (5, 10), (255, 128), (42, 123)
            };

            foreach (var (a, b) in testCases)
            {
                var result1 = GF256Add(a, b);
                var result2 = GF256Add(b, a);
                Assert.AreEqual(result1, result2, $"Commutativity failed for {a} + {b}");
            }
        }

        [TestMethod]
        public void GF256Add_AssociativeProperty()
        {
            var testCases = new (byte, byte, byte)[]
            {
                (1, 2, 3), (10, 20, 30), (255, 128, 64), (5, 15, 25)
            };

            foreach (var (a, b, c) in testCases)
            {
                var result1 = GF256Add(GF256Add(a, b), c);
                var result2 = GF256Add(a, GF256Add(b, c));
                Assert.AreEqual(result1, result2, $"Associativity failed for ({a} + {b}) + {c}");
            }
        }

        [TestMethod]
        public void GF256Add_IdentityProperty()
        {
            for (byte a = 0; a < 256; a++)
            {
                var result = GF256Add(a, 0);
                Assert.AreEqual(a, result, $"Identity property failed for {a}");
            }
        }

        [TestMethod]
        public void GF256Add_InverseProperty()
        {
            for (byte a = 0; a < 256; a++)
            {
                var result = GF256Add(a, a);
                Assert.AreEqual((byte)0, result, $"Inverse property failed for {a}");
            }
        }

        [TestMethod]
        public void GF256Multiply_CommutativeProperty()
        {
            var testCases = new (byte, byte)[]
            {
                (1, 1), (2, 3), (5, 10), (15, 20), (255, 2)
            };

            foreach (var (a, b) in testCases)
            {
                var result1 = GF256Multiply(a, b);
                var result2 = GF256Multiply(b, a);
                Assert.AreEqual(result1, result2, $"Commutativity failed for {a} * {b}");
            }
        }

        [TestMethod]
        public void GF256Multiply_AssociativeProperty()
        {
            var testCases = new (byte, byte, byte)[]
            {
                (2, 3, 4), (5, 6, 7), (10, 15, 20)
            };

            foreach (var (a, b, c) in testCases)
            {
                var result1 = GF256Multiply(GF256Multiply(a, b), c);
                var result2 = GF256Multiply(a, GF256Multiply(b, c));
                Assert.AreEqual(result1, result2, $"Associativity failed for ({a} * {b}) * {c}");
            }
        }

        [TestMethod]
        public void GF256Multiply_DistributiveProperty()
        {
            var testCases = new (byte, byte, byte)[]
            {
                (2, 3, 4), (5, 10, 15), (7, 11, 13)
            };

            foreach (var (a, b, c) in testCases)
            {
                var result1 = GF256Multiply(a, GF256Add(b, c));
                var result2 = GF256Add(GF256Multiply(a, b), GF256Multiply(a, c));
                Assert.AreEqual(result1, result2, $"Distributivity failed for {a} * ({b} + {c})");
            }
        }

        [TestMethod]
        public void GF256Multiply_IdentityProperty()
        {
            for (byte a = 0; a < 256; a++)
            {
                var result = GF256Multiply(a, 1);
                Assert.AreEqual(a, result, $"Identity property failed for {a}");
            }
        }

        [TestMethod]
        public void GF256Multiply_ZeroProperty()
        {
            for (byte a = 0; a < 256; a++)
            {
                var result = GF256Multiply(a, 0);
                Assert.AreEqual((byte)0, result, $"Zero property failed for {a}");
            }
        }

        [TestMethod]
        public void GF256Inverse_MultiplicativeIdentity()
        {
            for (byte a = 1; a < 256; a++)
            {
                var inverse = GF256Inverse(a);
                var result = GF256Multiply(a, inverse);
                Assert.AreEqual((byte)1, result, $"Multiplicative inverse failed for {a}");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void GF256Inverse_WithZero_ShouldThrow()
        {
            GF256Inverse(0);
        }

        [TestMethod]
        public void GF256Divide_ByOne_ShouldReturnOriginal()
        {
            for (byte a = 0; a < 256; a++)
            {
                var result = GF256Divide(a, 1);
                Assert.AreEqual(a, result, $"Division by 1 failed for {a}");
            }
        }

        [TestMethod]
        [ExpectedException(typeof(DivideByZeroException))]
        public void GF256Divide_ByZero_ShouldThrow()
        {
            GF256Divide(10, 0);
        }

        [TestMethod]
        public void GF256Divide_ZeroDividend_ShouldReturnZero()
        {
            for (byte b = 1; b < 256; b++)
            {
                var result = GF256Divide(0, b);
                Assert.AreEqual((byte)0, result, $"Zero dividend failed for divisor {b}");
            }
        }

        [TestMethod]
        public void GF256Divide_SelfDivision_ShouldReturnOne()
        {
            for (byte a = 1; a < 256; a++)
            {
                var result = GF256Divide(a, a);
                Assert.AreEqual((byte)1, result, $"Self division failed for {a}");
            }
        }

        [TestMethod]
        public void GF256Divide_MultiplyDivideInverse()
        {
            var testCases = new (byte, byte)[]
            {
                (10, 3), (25, 7), (100, 17), (255, 128)
            };

            foreach (var (a, b) in testCases)
            {
                var product = GF256Multiply(a, b);
                var result = GF256Divide(product, b);
                Assert.AreEqual(a, result, $"Multiply-divide inverse failed for {a} * {b} / {b}");
            }
        }

        [TestMethod]
        public void GF256Operations_EdgeValues()
        {
            byte[] edgeValues = { 0, 1, 2, 127, 128, 254, 255 };

            foreach (var a in edgeValues)
            {
                foreach (var b in edgeValues)
                {
                    var sum = GF256Add(a, b);
                    Assert.IsTrue(sum >= 0 && sum <= 255);

                    var product = GF256Multiply(a, b);
                    Assert.IsTrue(product >= 0 && product <= 255);

                    if (b != 0)
                    {
                        var quotient = GF256Divide(a, b);
                        Assert.IsTrue(quotient >= 0 && quotient <= 255);
                    }
                }
            }
        }
    }
}

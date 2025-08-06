using System.Security.Cryptography;
using Shamir.Ceremony.Common.Models;

namespace Shamir.Ceremony.Common.Cryptography;

public static class ShamirSecretShare
{
    private const int FieldSize = 256; // GF(2^8)

    public static List<Share> GenerateShares(byte[] secret, int threshold, int numShares)
    {
        var shares = new List<Share>();
        var coefficients = new byte[threshold][];

        coefficients[0] = secret;

        using var rng = RandomNumberGenerator.Create();
        for (int i = 1; i < threshold; i++)
        {
            coefficients[i] = new byte[secret.Length];
            rng.GetBytes(coefficients[i]);
        }

        for (int x = 1; x <= numShares; x++)
        {
            var y = new byte[secret.Length];

            for (int byteIndex = 0; byteIndex < secret.Length; byteIndex++)
            {
                byte result = coefficients[0][byteIndex];
                byte xPower = (byte)x;

                for (int degree = 1; degree < threshold; degree++)
                {
                    result = GF256Add(result, GF256Multiply(coefficients[degree][byteIndex], xPower));
                    xPower = GF256Multiply(xPower, (byte)x);
                }

                y[byteIndex] = result;
            }

            shares.Add(new Share { X = x, Y = Convert.ToBase64String(y) });
        }

        return shares;
    }

    public static byte[] ReconstructSecret(List<Share> shares, int threshold)
    {
        if (shares.Count < threshold)
            throw new ArgumentException($"Need at least {threshold} shares to reconstruct");

        var firstY = Convert.FromBase64String(shares[0].Y);
        var result = new byte[firstY.Length];

        for (int byteIndex = 0; byteIndex < firstY.Length; byteIndex++)
        {
            byte reconstructedByte = 0;

            for (int i = 0; i < threshold; i++)
            {
                byte yi = Convert.FromBase64String(shares[i].Y)[byteIndex];
                byte numerator = 1;
                byte denominator = 1;

                for (int j = 0; j < threshold; j++)
                {
                    if (i != j)
                    {
                        numerator = GF256Multiply(numerator, (byte)shares[j].X);
                        byte diff = GF256Add((byte)shares[i].X, (byte)shares[j].X);
                        denominator = GF256Multiply(denominator, diff);
                    }
                }

                byte lagrangeCoeff = GF256Divide(numerator, denominator);
                reconstructedByte = GF256Add(reconstructedByte, GF256Multiply(yi, lagrangeCoeff));
            }

            result[byteIndex] = reconstructedByte;
        }

        return result;
    }

    private static byte GF256Add(byte a, byte b) => (byte)(a ^ b);

    private static byte GF256Multiply(byte a, byte b)
    {
        if (a == 0 || b == 0) return 0;

        int result = 0;
        int temp = a;

        while (b != 0)
        {
            if ((b & 1) != 0)
                result ^= temp;

            temp <<= 1;
            if (temp >= 256)
                temp ^= 0x11b; // Reduction polynomial for GF(2^8)

            b >>= 1;
        }

        return (byte)result;
    }

    private static byte GF256Divide(byte a, byte b)
    {
        if (b == 0) throw new DivideByZeroException();
        if (a == 0) return 0;

        return GF256Multiply(a, GF256Inverse(b));
    }

    private static byte GF256Inverse(byte a)
    {
        if (a == 0) throw new ArgumentException("Zero has no inverse");

        for (byte b = 1; b < 255; b++)
        {
            if (GF256Multiply(a, b) == 1)
                return b;
        }

        throw new ArgumentException("No inverse found");
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using AllAAGMDStruct.IO;
using AllAAGMDStruct.GMD;

namespace AllAAGMDStruct
{
    public class XOR
    {
        private static List<string> key1 = new List<string> { "fjfajfahajra;tira9tgujagjjgajgoa", "e43bcc7fcab+a6c4ed22fcd433/9d2e6cb053fa462-463f3a446b19" };
        private static List<string> key2 = new List<string> { "mva;eignhpe/dfkfjgp295jtugkpejfu", "861f1dca05a0;9ddd5261e5dcc@6b438e6c.8ba7d71c*4fd11f3af1" };

        public static Stream DeXOR(Stream input) => DeXOR(new BinaryReaderX(input, true).ReadAllBytes());
        public static Stream DeXOR(byte[] input)
        {
            var lastByte = input[input.Length - 1];
            //if lastByte is 0, check if the key bytes produce 0 at that point by xor'ing
            if (lastByte == 0)
                if (!key1.Zip(key2, (k1, k2) => k1.Zip(k2, (x, y) => x == y).ToArray()[input.Length % k1.Length - 1]).Any(x => x))
                    return new MemoryStream(input);

            var t = input.Select((b, i) => (byte)(b ^ key1[1][i % key1[1].Length] ^ key2[1][i % key2[1].Length])).ToArray();

            int found = -1;
            for (int i = 0; i < key1.Count(); i++)
            {
                if ((lastByte ^ key1[i][(input.Length - 1) % key1[i].Length] ^ key2[i][(input.Length - 1) % key2[i].Length]) != 0)
                    continue;

                found = i;
            }

            if (found != -1)
            {
                return new MemoryStream(input.Select((b, i) => (byte)(b ^ key1[found][i % key1[found].Length] ^ key2[found][i % key2[found].Length])).ToArray());
            }
            else
            {
                throw new Exception("Data can't be deXOR'ed. File uses unknown keypair.");
            }
        }

        public static Stream ReXOR(Stream input, int keyPairID) => ReXOR(new BinaryReaderX(input, true).ReadAllBytes(), keyPairID);
        public static Stream ReXOR(byte[] input, int keyPairID) =>
            new MemoryStream(input.Select((b, i) => (byte)(b ^ key1[keyPairID][i % key1[keyPairID].Length] ^ key2[keyPairID][i % key2[keyPairID].Length])).ToArray());
    }
}

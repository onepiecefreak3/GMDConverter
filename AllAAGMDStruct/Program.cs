using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using AllAAGMDStruct.GMD;
using AllAAGMDStruct.Hash;

namespace AllAAGMDStruct
{
    class Program
    {
        static IGMD gmd;

        static void Main(string[] args)
        {
            if (args.Count() == 0)
                Environment.Exit(0);

            var res = Support.Identify(args[0]);
            if (res == Ident.NotFound)
            {
                Console.WriteLine($"File {args[0]} was not found!");
                Environment.Exit(0);
            }
            if (res == Ident.NotSupported)
            {
                Console.WriteLine("Provided GMD is not supported!");
                Environment.Exit(0);
            }

            if (res == Ident.v1)
                gmd = new GMDv1();
            if (res == Ident.v2)
                gmd = new GMDv2();

            gmd.Load(args[0]);

            gmd.Save(args[0] + ".bk", Platform.Mobile, Game.DD);
        }
    }
}

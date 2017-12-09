using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using AllAAGMDStruct.IO;

namespace AllAAGMDStruct.GMD
{
    public enum Ident : int
    {
        NotFound,
        NotSupported,
        v1 = 0x00010201,
        v2 = 0x00010302
    }
    public enum Platform : byte
    {
        CTR,
        WiiU,
        Mobile
    }
    public enum Game : byte
    {
        DD,
        SoJ,
        DGS1,
        DGS2
    }
    public enum Language : int
    {
        JAPANESE,
        ENGLISH,
        FRENCH,
        SPANISH,
        GERMAN,
        ITALIAN
    }

    public interface IGMD
    {
        GMDContent GMDContent { get; set; }

        void Load(string filename);
        void Save(string filename, Platform platform, Game game);
    }
    public class GMDContent
    {
        public string Name;
        public List<Content> Content = new List<Content>();
    }
    public class Content
    {
        public string Label;
        public string SectionText;
    }

    public class Support
    {
        public static Ident Identify(string file)
        {
            if (!File.Exists(file))
                return Ident.NotFound;

            using (var br = new BinaryReaderX(File.OpenRead(file)))
            {
                var mag = br.ReadString(4);
                var version = br.ReadUInt32();

                if (mag != "GMD")
                    return Ident.NotSupported;

                var existVers = new List<int> { 0x00010201, 0x00010302 };
                if (!existVers.Exists(ev => ev == version))
                    return Ident.NotSupported;

                return (Ident)version;
            }
        }
    }
}

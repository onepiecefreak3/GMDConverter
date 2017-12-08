using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AllAAGMDStruct.IO;
using AllAAGMDStruct.Hash;
using System.IO;

namespace AllAAGMDStruct.GMD
{
    //Version 1
    public class GMDv1 : IGMD
    {
        public GMDContent GMDContent { get; set; } = new GMDContent();

        #region Structs
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Header
        {
            public Magic Magic;
            public int Version;
            public Language Language;
            public long Zero1;
            public int LabelCount;
            public int SectionCount;
            public int LabelSize;
            public int SectionSize;
            public int NameSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Entry
        {
            public int ID;
            public int LabelOffset; //relative to LabelDataOffset and after subtracting (0x29080170 + Header.SectionCount * 0x80)
        }
        #endregion

        public void Load(string filename)
        {
            using (var br = new BinaryReaderX(File.OpenRead(filename)))
            {
                // Header
                var Header = br.ReadStruct<Header>();
                var Name = br.ReadCStringA();
                GMDContent.Name = Name;

                // Entries
                var Entries = br.ReadMultiple<Entry>(Header.SectionCount);
                var LabelDataOffset = (int)br.BaseStream.Position;

                // Labels
                var Names = new List<string>();
                foreach (var Entry in Entries)
                {
                    var LabelOffset = Entry.LabelOffset - (0x29080170 + Header.SectionCount * 0x80);
                    if (LabelOffset >= 0)
                    {
                        br.BaseStream.Position = LabelDataOffset + LabelOffset;
                        Names.Add(br.ReadCStringA());
                    }
                    else
                    {
                        Names.Add(String.Empty);
                    }
                }

                // Text
                br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.SectionCount * 0x8) + Header.LabelSize;
                var text = br.ReadBytes(Header.SectionSize);

                // Text deobfuscation
                var deXor = XOR.DeXOR(text);

                using (var brt = new BinaryReaderX(deXor))
                {
                    var counter = 0;
                    for (var i = 0; i < Header.SectionCount; i++)
                    {
                        var bk = brt.BaseStream.Position;
                        var tmp = brt.ReadByte();
                        while (tmp != 0)
                            tmp = brt.ReadByte();
                        var textSize = brt.BaseStream.Position - bk;
                        brt.BaseStream.Position = bk;

                        GMDContent.Content.Add(new Content
                        {
                            Label = Names[i] == String.Empty ? "no_name_" + counter++.ToString("000") : Names[i],
                            SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n"),
                            ID = i
                        });
                    }
                }
            }
        }

        public void Save(string filename, Platform platform, Game game)
        {
            //Get Text Section
            var TextBlob = Encoding.UTF8.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.SectionText + "\0").Replace("\r\n", "\xa").Replace("\xa", "\r\n")));

            //XOR, if needed
            if (platform == Platform.CTR && game == Game.DD)
                TextBlob = new BinaryReaderX(XOR.ReXOR(TextBlob, 0)).ReadAllBytes();

            //Get Label Blob
            var LabelBlob = Encoding.ASCII.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.Label.Contains("no_name") ? "" : c.Label + "\0")));

            //Create Entries
            var Entries = new List<Entry>();
            var LabelOffset = 0;
            foreach (var c in GMDContent.Content)
            {
                Entries.Add(new Entry
                {
                    ID = c.ID,
                    LabelOffset = (c.Label.Contains("no_name")) ? -1 : LabelOffset + (0x29080170 + GMDContent.Content.Count() * 0x80)
                });
                LabelOffset += (c.Label.Contains("no_name")) ? 0 : Encoding.ASCII.GetByteCount(c.Label) + 1;
            }

            //Header
            var Header = new Header
            {
                Magic = "GMD\0",
                Version = 0x00010201,
                Language = Language.ENGLISH,
                Zero1 = 0,
                LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name")),
                SectionCount = GMDContent.Content.Count(),
                LabelSize = LabelBlob.Length,
                SectionSize = TextBlob.Length,
                NameSize = Encoding.ASCII.GetByteCount(GMDContent.Name)
            };

            //Write stuff
            using (var bw = new BinaryWriterX(File.Create(filename)))
            {
                //Header
                bw.WriteStruct(Header);
                bw.Write(Encoding.ASCII.GetBytes(GMDContent.Name + "\0"));

                //Entries
                foreach (var entry in Entries)
                    bw.WriteStruct(entry);

                //Labels
                bw.Write(LabelBlob);

                //Text Sections
                bw.Write(TextBlob);
            }
        }
    }

    //Version 2
    public class GMDv2 : IGMD
    {
        public GMDContent GMDContent { get; set; } = new GMDContent();

        #region Structs
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Header
        {
            public Magic Magic;
            public int Version;
            public Language Language;
            public long Zero1;
            public int LabelCount;
            public int SectionCount;
            public int LabelSize;
            public int SectionSize;
            public int NameSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class Entry
        {
            public int ID;
            public uint Hash1;
            public uint Hash2;
            public int LabelOffset;
            public int ListLink;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class EntryMobile
        {
            public int ID;
            public uint Hash1;
            public uint Hash2;
            public int ZeroPadding;
            public long LabelOffset;
            public long ListLink;
        }
        #endregion

        public void Load(string filename)
        {
            using (var br = new BinaryReaderX(File.OpenRead(filename)))
            {
                // Header
                var Header = br.ReadStruct<Header>();
                var Name = br.ReadCStringA();
                GMDContent.Name = Name;

                //Check for platform difference
                var fullSize = 0x28 + Name.Length + 1 + Header.SectionCount * 0x14 + Header.LabelSize + Header.SectionSize;
                if (fullSize != br.BaseStream.Length)
                {
                    //Mobile structure

                    //Entry
                    var Entries = br.ReadMultiple<EntryMobile>(Header.SectionCount);

                    //Bucketlist
                    var Buckets = br.ReadMultiple<long>(0x100);
                    var LabelDataOffset = (int)br.BaseStream.Position;

                    // Labels
                    var Names = new List<string>();
                    foreach (var Entry in Entries)
                    {
                        if (Entry.LabelOffset >= 0)
                        {
                            br.BaseStream.Position = LabelDataOffset + Entry.LabelOffset;
                            Names.Add(br.ReadCStringA());
                        }
                        else
                        {
                            Names.Add(String.Empty);
                        }
                    }

                    // Text
                    br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.SectionCount * 0x20 + 0x100 * 0x8) + Header.LabelSize;
                    var text = br.ReadBytes(Header.SectionSize);

                    // Text deobfuscation
                    var deXor = XOR.DeXOR(text);

                    using (var brt = new BinaryReaderX(deXor))
                    {
                        var counter = 0;
                        for (var i = 0; i < Header.SectionCount; i++)
                        {
                            var bk = brt.BaseStream.Position;
                            var tmp = brt.ReadByte();
                            while (tmp != 0)
                                tmp = brt.ReadByte();
                            var textSize = brt.BaseStream.Position - bk;
                            brt.BaseStream.Position = bk;

                            GMDContent.Content.Add(new Content
                            {
                                Label = Names[i] == String.Empty ? "no_name_" + counter++.ToString("000") : Names[i],
                                SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n"),
                                ID = i
                            });
                        }
                    }
                }
                else
                {
                    //CTR structure

                    //Entry
                    var Entries = br.ReadMultiple<Entry>(Header.SectionCount);

                    //Bucketlist
                    var Buckets = br.ReadMultiple<int>(0x100);
                    var LabelDataOffset = (int)br.BaseStream.Position;

                    // Labels
                    var Names = new List<string>();
                    foreach (var Entry in Entries)
                    {
                        if (Entry.LabelOffset >= 0)
                        {
                            br.BaseStream.Position = LabelDataOffset + Entry.LabelOffset;
                            Names.Add(br.ReadCStringA());
                        }
                        else
                        {
                            Names.Add(String.Empty);
                        }
                    }

                    // Text
                    br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.SectionCount * 0x14 + 0x100 * 0x4) + Header.LabelSize;
                    var text = br.ReadBytes(Header.SectionSize);

                    // Text deobfuscation
                    var deXor = XOR.DeXOR(text);

                    using (var brt = new BinaryReaderX(deXor))
                    {
                        var counter = 0;
                        for (var i = 0; i < Header.SectionCount; i++)
                        {
                            var bk = brt.BaseStream.Position;
                            var tmp = brt.ReadByte();
                            while (tmp != 0)
                                tmp = brt.ReadByte();
                            var textSize = brt.BaseStream.Position - bk;
                            brt.BaseStream.Position = bk;

                            GMDContent.Content.Add(new Content
                            {
                                Label = Names[i] == String.Empty ? "no_name_" + counter++.ToString("000") : Names[i],
                                SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n"),
                                ID = i
                            });
                        }
                    }
                }
            }
        }

        public void Save(string filename, Platform platform, Game game)
        {
            //Get Text Blob
            var TextBlob = Encoding.UTF8.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.Label.Contains("no_name") ? "" : c.SectionText.Replace("\r\n", "\xa").Replace("\xa", "\r\n") + "\0")));

            //ReXOR, if needed
            if (platform == Platform.CTR && game == Game.DGS2)
                TextBlob = new BinaryReaderX(XOR.ReXOR(TextBlob, 1)).ReadAllBytes();

            //Get Label Blob
            var LabelBlob = Encoding.ASCII.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.Label.Contains("no_name") ? "" : c.Label + "\0")));

            if (platform == Platform.Mobile)
            {
                //Create Entries
                var Entries = new List<EntryMobile>();
                var Buckets = new Dictionary<byte, int>();
                int LabelOffset = 0;
                foreach (var c in GMDContent.Content)
                {
                    Entries.Add(new EntryMobile
                    {
                        ID = c.ID,
                        Hash1 = ~Crc32.Create(c.Label + c.Label),
                        Hash2 = ~Crc32.Create(c.Label + c.Label + c.Label),
                        ZeroPadding = 0,
                        LabelOffset = (c.Label.Contains("no_name")) ? -1 : LabelOffset,
                        ListLink = 0
                    });
                    LabelOffset += (c.Label.Contains("no_name")) ? 0 : Encoding.ASCII.GetByteCount(c.Label) + 1;

                    var bucket = (byte)(~Crc32.Create(c.Label) & 0xff);
                    if (Buckets.ContainsKey(bucket))
                    {
                        Entries[Buckets[bucket]].ListLink = c.ID;
                        Buckets[bucket] = c.ID;
                    }
                    else
                    {
                        Buckets.Add(bucket, c.ID);
                    }
                }

                //Create bucketList Blob
                var BucketBlob = new long[0x100];
                foreach (var c in GMDContent.Content)
                {
                    var bucket = (byte)(~Crc32.Create(c.Label) & 0xff);
                    if (BucketBlob[bucket] == 0)
                        BucketBlob[bucket] = (c.ID == 0) ? -1 : c.ID;
                }

                //Create Header
                var Header = new Header
                {
                    Magic = "GMD\0",
                    Version = 0x00010302,
                    Language = Language.ENGLISH,
                    Zero1 = 0,
                    LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name")),
                    SectionCount = GMDContent.Content.Count(),
                    LabelSize = LabelBlob.Length,
                    SectionSize = TextBlob.Length,
                    NameSize = Encoding.ASCII.GetByteCount(GMDContent.Name)
                };

                //Write Stuff
                using (var bw = new BinaryWriterX(File.Create(filename)))
                {
                    //Header
                    bw.WriteStruct(Header);
                    bw.Write(Encoding.ASCII.GetBytes(GMDContent.Name + "\0"));

                    //Entries
                    foreach (var entry in Entries)
                        bw.WriteStruct(entry);

                    //BucketList
                    foreach (var bucket in BucketBlob)
                        bw.Write(bucket);

                    //Labels
                    bw.Write(LabelBlob);

                    //Text Sections
                    bw.Write(TextBlob);
                }
            }
            else if (platform == Platform.CTR)
            {
                //Create Entries
                var Entries = new List<Entry>();
                var Buckets = new Dictionary<byte, int>();
                int LabelOffset = 0;
                foreach (var c in GMDContent.Content)
                {
                    Entries.Add(new Entry
                    {
                        ID = c.ID,
                        Hash1 = ~Crc32.Create(c.Label + c.Label),
                        Hash2 = ~Crc32.Create(c.Label + c.Label + c.Label),
                        LabelOffset = (c.Label.Contains("no_name")) ? -1 : LabelOffset,
                        ListLink = 0
                    });
                    LabelOffset += (c.Label.Contains("no_name")) ? 0 : Encoding.ASCII.GetByteCount(c.Label) + 1;

                    var bucket = (byte)(~Crc32.Create(c.Label) & 0xff);
                    if (Buckets.ContainsKey(bucket))
                    {
                        Entries[Buckets[bucket]].ListLink = c.ID;
                        Buckets[bucket] = c.ID;
                    }
                    else
                    {
                        Buckets.Add(bucket, c.ID);
                    }
                }

                //Create bucketList Blob
                var BucketBlob = new int[0x100];
                foreach (var c in GMDContent.Content)
                {
                    var bucket = (byte)(~Crc32.Create(c.Label) & 0xff);
                    if (BucketBlob[bucket] == 0)
                        BucketBlob[bucket] = (c.ID == 0) ? -1 : c.ID;
                }

                //Create Header
                var Header = new Header
                {
                    Magic = "GMD\0",
                    Version = 0x00010302,
                    Language = Language.ENGLISH,
                    Zero1 = 0,
                    LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name")),
                    SectionCount = GMDContent.Content.Count(),
                    LabelSize = LabelBlob.Length,
                    SectionSize = TextBlob.Length,
                    NameSize = Encoding.ASCII.GetByteCount(GMDContent.Name)
                };

                //Write Stuff
                using (var bw = new BinaryWriterX(File.Create(filename)))
                {
                    //Header
                    bw.WriteStruct(Header);
                    bw.Write(Encoding.ASCII.GetBytes(GMDContent.Name + "\0"));

                    //Entries
                    foreach (var entry in Entries)
                        bw.WriteStruct(entry);

                    //BucketList
                    foreach (var bucket in BucketBlob)
                        bw.Write(bucket);

                    //Labels
                    bw.Write(LabelBlob);

                    //Text Sections
                    bw.Write(TextBlob);
                }
            }
        }
    }
}

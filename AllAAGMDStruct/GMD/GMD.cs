﻿using System;
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
        public class LabelEntry
        {
            public int SectionID;
            public int LabelOffset; //relative to LabelDataOffset and after subtracting (0x29080170 + Header.LabelCount * 0x80)
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

                // Label Entries
                var LabelEntries = (Header.LabelCount > 0) ? br.ReadMultiple<LabelEntry>(Header.LabelCount) : new List<LabelEntry>();
                var LabelDataOffset = (int)br.BaseStream.Position;

                // Text
                br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.LabelCount * 0x8) + Header.LabelSize;
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

                        //Get Label if existent
                        var label = "";
                        if (LabelEntries.Find(l => l.SectionID == i) != null)
                        {
                            bk = br.BaseStream.Position;
                            br.BaseStream.Position = LabelDataOffset + (LabelEntries.Find(l => l.SectionID == i).LabelOffset - (0x29080170 + Header.LabelCount * 0x80));
                            label = br.ReadCStringA();
                            br.BaseStream.Position = bk;
                        }

                        GMDContent.Content.Add(new Content
                        {
                            Label = label == "" ? "no_name_" + counter++.ToString("000") : label,
                            SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n")
                        });
                    }
                }
            }
        }

        public void Save(string filename, Platform platform, Game game)
        {
            //Get Text Section
            var TextBlob = Encoding.UTF8.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + c.SectionText.Replace("\r\n", "\xa").Replace("\xa", "\r\n") + "\0"));

            //XOR, if needed
            if (platform == Platform.CTR && game == Game.DD)
                TextBlob = new BinaryReaderX(XOR.ReXOR(TextBlob, 0)).ReadAllBytes();

            //Get Label Blob
            var LabelBlob = Encoding.ASCII.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.Label.Contains("no_name") ? "" : c.Label + "\0")));

            //Create LabelEntries
            var Entries = new List<LabelEntry>();
            var LabelOffset = 0;
            var LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name"));
            for (int i = 0; i < GMDContent.Content.Count(); i++)
            {
                if (!GMDContent.Content[i].Label.Contains("no_name"))
                {
                    Entries.Add(new LabelEntry
                    {
                        SectionID = i,
                        LabelOffset = LabelOffset + (0x29080170 + LabelCount * 0x80)
                    });
                    LabelOffset += Encoding.ASCII.GetByteCount(GMDContent.Content[i].Label) + 1;
                }
            }

            //Header
            var Header = new Header
            {
                Magic = "GMD\0",
                Version = 0x00010201,
                Language = Language.ENGLISH,
                Zero1 = 0,
                LabelCount = LabelCount,
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

                //LabelEntries
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
            public long Zero1 = 0;
            public int LabelCount;
            public int SectionCount;
            public int LabelSize;
            public int SectionSize;
            public int NameSize;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class LabelEntry
        {
            public int SectionID;
            public uint Hash1;
            public uint Hash2;
            public int LabelOffset;
            public int ListLink;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public class LabelEntryMobile
        {
            public int SectionID;
            public uint Hash1;
            public uint Hash2;
            public int ZeroPadding = 0;
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
                var fullSize = 0x28 + Name.Length + 1 + Header.LabelCount * 0x14 + ((Header.LabelCount > 0) ? 0x100 * 0x4 : 0) + Header.LabelSize + Header.SectionSize;
                if (fullSize != br.BaseStream.Length)
                {
                    //Mobile structure

                    //Entry
                    var LabelEntries = (Header.LabelCount > 0) ? br.ReadMultiple<LabelEntryMobile>(Header.LabelCount) : new List<LabelEntryMobile>();

                    //Bucketlist
                    var Buckets = (Header.LabelCount > 0) ? br.ReadMultiple<long>(0x100) : new List<long>();
                    var LabelDataOffset = (int)br.BaseStream.Position;

                    // Text
                    br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.LabelCount * 0x20 + ((Header.LabelCount > 0) ? 0x100 * 0x8 : 0)) + Header.LabelSize;
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

                            //Get Label if existent
                            var label = "";
                            if (LabelEntries.Find(l => l.SectionID == i) != null)
                            {
                                bk = br.BaseStream.Position;
                                br.BaseStream.Position = LabelDataOffset + LabelEntries.Find(l => l.SectionID == i).LabelOffset;
                                label = br.ReadCStringA();
                                br.BaseStream.Position = bk;
                            }

                            GMDContent.Content.Add(new Content
                            {
                                Label = label == "" ? "no_name_" + counter++.ToString("000") : label,
                                SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n")
                            });
                        }
                    }
                }
                else
                {
                    //CTR structure

                    //Entry
                    var LabelEntries = (Header.LabelCount > 0) ? br.ReadMultiple<LabelEntry>(Header.LabelCount) : new List<LabelEntry>();

                    //Bucketlist
                    var Buckets = (Header.LabelCount > 0) ? br.ReadMultiple<int>(0x100) : new List<int>();
                    var LabelDataOffset = (int)br.BaseStream.Position;

                    // Text
                    br.BaseStream.Position = 0x28 + (Header.NameSize + 1) + (Header.LabelCount * 0x14 + ((Header.LabelCount > 0) ? 0x100 * 0x4 : 0)) + Header.LabelSize;
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

                            //Get Label if existent
                            var label = "";
                            if (LabelEntries.Find(l => l.SectionID == i) != null)
                            {
                                bk = br.BaseStream.Position;
                                br.BaseStream.Position = LabelDataOffset + LabelEntries.Find(l => l.SectionID == i).LabelOffset;
                                label = br.ReadCStringA();
                                br.BaseStream.Position = bk;
                            }

                            GMDContent.Content.Add(new Content
                            {
                                Label = label == "" ? "no_name_" + counter++.ToString("000") : label,
                                SectionText = brt.ReadString((int)textSize, Encoding.UTF8).Replace("\r\n", "\xa").Replace("\xa", "\r\n")
                            });
                        }
                    }
                }
            }
        }

        public void Save(string filename, Platform platform, Game game)
        {
            //Get Text Blob
            var TextBlob = Encoding.UTF8.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + c.SectionText.Replace("\r\n", "\xa").Replace("\xa", "\r\n") + "\0"));

            //ReXOR, if needed
            if (platform == Platform.CTR && game == Game.DGS2)
                TextBlob = new BinaryReaderX(XOR.ReXOR(TextBlob, 1)).ReadAllBytes();

            //Get Label Blob
            var LabelBlob = Encoding.ASCII.GetBytes(GMDContent.Content.Aggregate("", (output, c) => output + (c.Label.Contains("no_name") ? "" : c.Label + "\0")));

            if (platform == Platform.Mobile)
            {
                //Create Entries
                var LabelEntries = new List<LabelEntryMobile>();
                var Buckets = new Dictionary<byte, int>();
                int LabelOffset = 0;
                var LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name"));
                var counter = 0;
                for (var i = 0; i < GMDContent.Content.Count(); i++)
                {
                    if (!GMDContent.Content[i].Label.Contains("no_name"))
                    {
                        LabelEntries.Add(new LabelEntryMobile
                        {
                            SectionID = i,
                            Hash1 = ~Crc32.Create(GMDContent.Content[i].Label + GMDContent.Content[i].Label),
                            Hash2 = ~Crc32.Create(GMDContent.Content[i].Label + GMDContent.Content[i].Label + GMDContent.Content[i].Label),
                            LabelOffset = LabelOffset,
                            ListLink = 0
                        });
                        LabelOffset += Encoding.ASCII.GetByteCount(GMDContent.Content[i].Label) + 1;

                        var bucket = (byte)(~Crc32.Create(GMDContent.Content[i].Label) & 0xff);
                        if (Buckets.ContainsKey(bucket))
                        {
                            LabelEntries[Buckets[bucket]].ListLink = counter;
                            Buckets[bucket] = counter;
                        }
                        else
                        {
                            Buckets.Add(bucket, counter);
                        }
                        counter++;
                    }
                }

                //Create bucketList Blob
                var BucketBlob = new long[0x100];
                if (LabelCount > 0)
                {
                    var counter2 = 0;
                    for (var i = 0; i < GMDContent.Content.Count(); i++)
                    {
                        if (!GMDContent.Content[i].Label.Contains("no_name"))
                        {
                            var bucket = (byte)(~Crc32.Create(GMDContent.Content[i].Label) & 0xff);
                            if (BucketBlob[bucket] == 0)
                                BucketBlob[bucket] = (counter2 == 0) ? -1 : counter2;
                            counter2++;
                        }
                    }
                }

                //Create Header
                var Header = new Header
                {
                    Magic = "GMD\0",
                    Version = 0x00010302,
                    Language = Language.ENGLISH,
                    LabelCount = LabelCount,
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
                    foreach (var entry in LabelEntries)
                        bw.WriteStruct(entry);

                    //BucketList
                    if (LabelCount > 0)
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
                var LabelEntries = new List<LabelEntry>();
                var Buckets = new Dictionary<byte, int>();
                int LabelOffset = 0;
                var LabelCount = GMDContent.Content.Count(c => !c.Label.Contains("no_name"));
                var counter = 0;
                for (var i = 0; i < GMDContent.Content.Count(); i++)
                {
                    if (!GMDContent.Content[i].Label.Contains("no_name"))
                    {
                        LabelEntries.Add(new LabelEntry
                        {
                            SectionID = i,
                            Hash1 = ~Crc32.Create(GMDContent.Content[i].Label + GMDContent.Content[i].Label),
                            Hash2 = ~Crc32.Create(GMDContent.Content[i].Label + GMDContent.Content[i].Label + GMDContent.Content[i].Label),
                            LabelOffset = LabelOffset,
                            ListLink = 0
                        });
                        LabelOffset += Encoding.ASCII.GetByteCount(GMDContent.Content[i].Label) + 1;

                        var bucket = (byte)(~Crc32.Create(GMDContent.Content[i].Label) & 0xff);
                        if (Buckets.ContainsKey(bucket))
                        {
                            LabelEntries[Buckets[bucket]].ListLink = counter;
                            Buckets[bucket] = counter;
                        }
                        else
                        {
                            Buckets.Add(bucket, counter);
                        }
                        counter++;
                    }
                }

                //Create bucketList Blob
                var BucketBlob = new int[0x100];
                if (LabelCount > 0)
                {
                    var counter2 = 0;
                    for (var i = 0; i < GMDContent.Content.Count(); i++)
                    {
                        if (!GMDContent.Content[i].Label.Contains("no_name"))
                        {
                            var bucket = (byte)(~Crc32.Create(GMDContent.Content[i].Label) & 0xff);
                            if (BucketBlob[bucket] == 0)
                                BucketBlob[bucket] = (counter2 == 0) ? -1 : counter2;
                            counter2++;
                        }
                    }
                }

                //Create Header
                var Header = new Header
                {
                    Magic = "GMD\0",
                    Version = 0x00010302,
                    Language = Language.ENGLISH,
                    LabelCount = LabelCount,
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
                    foreach (var entry in LabelEntries)
                        bw.WriteStruct(entry);

                    //BucketList
                    if (LabelCount > 0)
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

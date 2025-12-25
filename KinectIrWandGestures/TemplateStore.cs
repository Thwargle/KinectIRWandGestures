using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace KinectIrWandGestures
{
    public sealed class TemplateStore
    {
        public string FilePath { get; }

        public TemplateStore(string filePath)
        {
            FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        }

        public List<SpellTemplate> LoadOrEmpty()
        {
            try
            {
                if (!File.Exists(FilePath))
                    return new List<SpellTemplate>();

                using (var fs = File.OpenRead(FilePath))
                {
                    var ser = new DataContractJsonSerializer(typeof(List<SpellTemplate>));
                    var obj = ser.ReadObject(fs) as List<SpellTemplate>;
                    return obj ?? new List<SpellTemplate>();
                }
            }
            catch
            {
                return new List<SpellTemplate>();
            }
        }

        public void Save(List<SpellTemplate> templates)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            using (var fs = File.Create(FilePath))
            {
                var ser = new DataContractJsonSerializer(typeof(List<SpellTemplate>));
                ser.WriteObject(fs, templates);
            }
        }
    }

    [DataContract]
    public sealed class SpellTemplate
    {
        [DataMember(Order = 1)]
        public string Name { get; set; }

        [DataMember(Order = 2)]
        public List<XY> Points { get; set; }
    }

    [DataContract]
    public sealed class XY
    {
        [DataMember(Order = 1)]
        public double X { get; set; }

        [DataMember(Order = 2)]
        public double Y { get; set; }
    }
}

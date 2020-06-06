using System;
using System.Text.Json;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace FeatureTest
{
    class Program
    {
        static void Main(string[] args)
        {
            //new System.Text.Json.JsonSerializer();



            //ArraySegment<byte> seg = new ArraySegment<byte>(new byte[100], 0, 10);
            //var span = new Memory<byte>(new byte[100]).Slice(0, 10);
            //string s = JsonConvert.SerializeObject(new Memory<byte>(new byte[100]).Slice(0, 10));
            //var seg2 = JsonConvert.DeserializeObject<byte[]>(s);

            var obj = new TestMe()
            {
                Args = new object[] { "qwer", 123L, new Test3() }
            };

            var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(obj);

            var doc = JsonDocument.Parse(bytes);
            //doc.RootElement.GetProperty("Args")
            var reader = new Utf8JsonReader(bytes);
            //reader.valu
            reader.Read();
            reader.Read();

            //doc.RootElement.GetProperty("a").

            var res = System.Text.Json.JsonSerializer.Deserialize<TestMe2>(ref reader);

            //var a = JsonSerializer.Deserialize(res.Args[2].GetRawText());

            //var obj2 = System.Text.Json.JsonSerializer.Deserialize<Test3>(a);
            //JsonProperty jp;
            //JsonSerializer.Deserialize(jp.Value, typeof(int));

            //new Utf8JsonReader()
            
            //JsonSerializer.Deserialize()
        }
    }


    class TestMe
    {
        public object[] Args { get; set; }
    }

    class TestMe2
    {
        public JsonElement[] Args { get; set; }
    }

    class Test3
    {
        public string Test { get; set; } = "Henlo";
    }
}

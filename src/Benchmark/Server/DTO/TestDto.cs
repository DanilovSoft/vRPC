using System.Runtime.Serialization;

namespace Server
{
    public class TestDto
    {
        [DataMember]
        public string Text { get; set; }

        public TestDto(string text)
        {
            Text = text;
        }

        private TestDto()
        {

        }
    }
}

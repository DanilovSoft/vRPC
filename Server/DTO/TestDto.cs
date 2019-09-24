using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;

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

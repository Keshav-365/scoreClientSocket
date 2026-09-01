using System;
using System.Collections.Generic;
using System.Text;

namespace Modal
{
    public class clsResponse
    {
        public bool success { get; set; }
        public object data { get; set; }
        public string message { get; set; }
        public int status { get; set; }
    }
}

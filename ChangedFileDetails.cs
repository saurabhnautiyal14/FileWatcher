using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileWatcher
{
    public class ChangedFileDetails
    {
        public string File { get; set; }
        public string Type { get; set; }
        public string OldFile { get; set; }
    }
}

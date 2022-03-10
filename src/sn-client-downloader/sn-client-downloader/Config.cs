using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace sn_client_downloader
{
    internal class Config
    {
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public string? RepoURL { get; set; }
        public int? RetryMillisecond { get; set; }
        public int? RetryCount { get; set; }
        public Target[]? Target { get; set; }
    }
}

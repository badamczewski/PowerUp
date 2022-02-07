using System;
using System.Collections.Generic;
using System.Reflection;

namespace PowerUp.Watcher
{
    public class ConsoleCommand
    {
        public string Command { get; set; }
        public List<ConsoleInputAttribute> InputOptions { get; set; } = new List<ConsoleInputAttribute>();
        public List<PropertyInfo> InputProperties { get; set; } = new List<PropertyInfo>();
        public List<object> InputValues { get; set; } = new List<object>();
        public Delegate Call { get; set; }
    }
}

// FileWatcher should now be watching and auto-index this
using System;

namespace TestNowWatching
{
    public class NowWatchingTest
    {
        public void TestAutoIndexing()
        {
            Console.WriteLine("FileWatcher is now watching this workspace");
            Console.WriteLine("This file should be auto-indexed!");
        }
    }
}
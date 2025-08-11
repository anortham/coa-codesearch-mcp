// Testing FileWatcher auto-indexing after workspace is being watched
using System;

namespace TestAutoIndexNow
{
    public class AutoIndexNowTest
    {
        public void TestAutoIndexing()
        {
            Console.WriteLine("FileWatcher should detect and index this file");
            Console.WriteLine("Created at: " + DateTime.Now);
        }
    }
}
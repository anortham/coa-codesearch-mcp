using System;

namespace FuzzyDemo
{
    public class UserService
    {
        // Original method
        public void getUserData()
        {
            Console.WriteLine("Getting user data");
        }

        // Method with typo - missing 'a'
        public void getUserDat()
        {
            Console.WriteLine("Typo version");
        }

        // Method with extra space
        public void getUserData ()
        {
            Console.WriteLine("Extra space version");
        }

        // Method with slight variation
        public void getUserDatta()
        {
            Console.WriteLine("Double 't' typo");
        }

        // Caller methods
        public void ProcessUser()
        {
            getUserData();
            getUserDat();
            getUserData ();
        }
    }
}

using System;

namespace MixedIndentationTest
{
    public class IndentationMixer
    {
        public void Method1()
        {
	// New comment with tab
            var variable1 = "test";
	// New comment with tab
        }

	public void Method2()
	{
	    	// New comment with tab
		var variable2 = "test";
	    	// New comment with tab
	}
    }
}
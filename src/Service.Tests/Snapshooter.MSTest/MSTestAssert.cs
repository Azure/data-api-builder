using Snapshooter.Core;

namespace Snapshooter.MSTest
{
    /// <summary>
    /// The XunitAssert instance compares two strings with the XUnit assert utility.
    /// </summary>
    public class MSTestAssert : IAssert
    {
        /// <summary>
        /// Asserts the expected snapshot and the actual snapshot 
        /// with the XUnit assert utility.
        /// </summary>
        /// <param name="expectedSnapshot">The expected snapshot.</param>
        /// <param name="actualSnapshot">The actual snapshot.</param>
        public void Assert(string expectedSnapshot, string actualSnapshot)
        {
            Microsoft.VisualStudio.TestTools.UnitTesting.Assert.AreEqual(expectedSnapshot, actualSnapshot);
        }
    }
}

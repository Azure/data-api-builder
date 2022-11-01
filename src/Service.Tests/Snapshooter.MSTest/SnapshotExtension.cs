using System;

namespace Snapshooter.MSTest
{
    public static class SnapshotExtension
    {
        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void MatchSnapshot(
            this object currentResult,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            object cleanedObject = currentResult.RemoveUnwantedWrappers();
            Snapshot.Match(cleanedObject, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotNameExtension">
        /// The snapshot name extension will extend the generated snapshot name with
        /// this given extensions. It can be used to make a snapshot name even more
        /// specific.
        /// Example:
        /// Generated Snapshotname = 'NumberAdditionTest'
        /// Snapshot name extension = '5', '6', 'Result', '11'
        /// Result: 'NumberAdditionTest_5_6_Result_11'
        /// </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void MatchSnapshot(
            this object currentResult,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            object cleanedObject = currentResult.RemoveUnwantedWrappers();
            Snapshot.Match(cleanedObject, snapshotNameExtension, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotName">
        /// The name of the snapshot. If not set, then the snapshotname
        /// will be evaluated automatically from the mstest test name.
        /// </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void MatchSnapshot(
            this object currentResult,
            string snapshotName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            object cleanedObject = currentResult.RemoveUnwantedWrappers();
            Snapshot.Match(cleanedObject, snapshotName, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotName">
        /// The name of the snapshot. If not set, then the snapshotname
        /// will be evaluated automatically from the mstest test name.
        /// </param>
        /// <param name="snapshotNameExtension">
        /// The snapshot name extension will extend the generated snapshot name with
        /// this given extensions. It can be used to make a snapshot name even more
        /// specific.
        /// Example:
        /// Generated Snapshotname = 'NumberAdditionTest'
        /// Snapshot name extension = '5', '6', 'Result', '11'
        /// Result: 'NumberAdditionTest_5_6_Result_11'
        /// </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison.
        /// </param>
        public static void MatchSnapshot(
            this object currentResult,
            string snapshotName,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            object cleanedObject = currentResult.RemoveUnwantedWrappers();
            Snapshot.Match(cleanedObject, snapshotName, snapshotNameExtension, matchOptions);
        }

        /// <summary>        
        /// Creates a json snapshot of the given object and compares it with the 
        /// already existing snapshot of the test. 
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotFullName">
        /// The full name of a snapshot with folder and file name. 
        /// To get a SnapshotFullName use Snapshot.FullName(). </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison.
        /// </param>
        public static void MatchSnapshot(
            this object currentResult,
            SnapshotFullName snapshotFullName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            object cleanedObject = currentResult.RemoveUnwantedWrappers();
            Snapshot.Match(cleanedObject, snapshotFullName, matchOptions);
        }
    }
}

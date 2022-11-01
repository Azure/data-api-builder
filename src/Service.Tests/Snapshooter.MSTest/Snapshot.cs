using System;
using System.Threading;
using Snapshooter.Core;
using Snapshooter.Core.Serialization;

namespace Snapshooter.MSTest
{
    /// <summary>
    /// The snapshot class creates and compares snapshots of object.
    /// It creates a json snapshot of the given object and compares it with the
    /// already existing snapshot of the test. If no snapshot exists already for this
    /// test, then a new snapshot will be created from the current result and saved
    /// in the folder __snapshots__ next to the executing test class file.
    /// </summary>
    public static class Snapshot
    {
        private static AsyncLocal<SnapshotFullName> _snapshotName = new();

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <typeparam name="T">The type of the result/object to match.</typeparam>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void Match<T>(
            T currentResult,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match((object)currentResult, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <typeparam name="T">The type of the result/object to match.</typeparam>
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
        public static void Match<T>(
            T currentResult,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match((object)currentResult, snapshotNameExtension, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <typeparam name="T">The type of the result/object to match.</typeparam>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotName">
        /// The name of the snapshot. If not set, then the snapshotname
        /// will be evaluated automatically from the xunit test name.
        /// </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void Match<T>(
            T currentResult,
            string snapshotName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match((object)currentResult, snapshotName, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <typeparam name="T">The type of the result/object to match.</typeparam>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotName">
        /// The name of the snapshot. If not set, then the snapshotname
        /// will be evaluated automatically from the xunit test name.
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
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void Match<T>(
            T currentResult,
            string snapshotName,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match((object)currentResult, snapshotName, snapshotNameExtension, matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotFullName">
        /// The full name of a snapshot with folder and file name.</param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison.
        /// </param>
        public static void Match<T>(
            T currentResult,
            SnapshotFullName snapshotFullName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match((object)currentResult, snapshotFullName, matchOptions);
        }

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
        public static void Match(
            object currentResult,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            try
            {
                Snapshooter.AssertSnapshot(currentResult, FullName(), matchOptions);
            }
            finally
            {
                _snapshotName = new AsyncLocal<SnapshotFullName>();
            }
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
        public static void Match(
            object currentResult,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match(currentResult, FullName(snapshotNameExtension), matchOptions);
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
        /// will be evaluated automatically from the xunit test name.
        /// </param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison
        /// </param>
        public static void Match(
            object currentResult,
            string snapshotName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match(currentResult, FullName(snapshotName), matchOptions);
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
        /// will be evaluated automatically from the xunit test name.
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
        public static void Match(
            object currentResult,
            string snapshotName,
            SnapshotNameExtension snapshotNameExtension,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            Match(currentResult, FullName(snapshotName, snapshotNameExtension), matchOptions);
        }

        /// <summary>
        /// Creates a json snapshot of the given object and compares it with the
        /// already existing snapshot of the test.
        /// If no snapshot exists, a new snapshot will be created from the current result
        /// and saved under a certain file path, which will shown within the test message.
        /// </summary>
        /// <param name="currentResult">The object to match.</param>
        /// <param name="snapshotFullName">
        /// The full name of a snapshot with folder and file name.</param>
        /// <param name="matchOptions">
        /// Additional compare actions, which can be applied during the snapshot comparison.
        /// </param>
        public static void Match(
            object currentResult,
            SnapshotFullName snapshotFullName,
            Func<MatchOptions, MatchOptions> matchOptions = null)
        {
            try
            {
                Snapshooter.AssertSnapshot(currentResult, snapshotFullName, matchOptions);
            }
            finally
            {
                _snapshotName = new AsyncLocal<SnapshotFullName>();
            }
        }

        /// <summary>
        /// Resolves automatically the snapshot name for the running unit test.
        /// </summary>
        /// <returns>The full name of a snapshot.</returns>
        public static SnapshotFullName FullName()
        {
            SnapshotFullName fullName = _snapshotName.Value;

            if (fullName is null)
            {
                fullName = Snapshooter.ResolveSnapshotFullName();
                _snapshotName.Value = fullName;
            }

            return fullName;
        }

        /// <summary>
        /// Resolves the snapshot name for the running unit test.
        /// The default generated snapshot name can be overwritten
        /// by the given snapshot name.
        /// </summary>
        /// <param name="snapshotName">
        /// The snapshot name given by the user. This snapshot name will overwrite
        /// the automatically generated snapshot name.
        /// </param>
        /// <returns>The full name of a snapshot.</returns>
        public static SnapshotFullName FullName(string snapshotName)
        {
            SnapshotFullName fullName = _snapshotName.Value;

            if (fullName is null)
            {
                fullName = Snapshooter.ResolveSnapshotFullName(snapshotName);
                _snapshotName.Value = fullName;
            }

            return fullName;
        }

        /// <summary>
        /// Resolves the snapshot name for the running unit test.
        /// The default generated snapshot name can be extended by
        /// the snapshot name extensions.
        /// </summary>
        /// <param name="snapshotNameExtension">
        /// The snapshot name extension will extend the snapshot name with
        /// this given extensions. It can be used to make a snapshot name even more
        /// specific.
        /// Example:
        /// Snapshot name = 'NumberAdditionTest'
        /// Snapshot name extension = '5', '6', 'Result', '11'
        /// Result: 'NumberAdditionTest_5_6_Result_11'
        /// </param>
        /// <returns>The full name of a snapshot.</returns>
        public static SnapshotFullName FullName(
            SnapshotNameExtension snapshotNameExtension)
        {
            SnapshotFullName fullName = _snapshotName.Value;

            if (fullName is null)
            {
                fullName = Snapshooter.ResolveSnapshotFullName(
                    snapshotNameExtension: snapshotNameExtension);
                _snapshotName.Value = fullName;
            }

            return fullName;
        }

        /// <summary>
        /// Resolves the snapshot name for the running unit test.
        /// The default generated snapshot name can either be overwritten
        /// with a given snapshot name, or can be extended by the snapshot name extensions,
        /// or both.
        /// </summary>
        /// <param name="snapshotName">
        /// The snapshot name given by the user, this snapshot name will overwrite
        /// the automatically generated snapshot name.
        /// </param>
        /// <param name="snapshotNameExtension">
        /// The snapshot name extension will extend the snapshot name with
        /// this given extensions. It can be used to make a snapshot name even more
        /// specific.
        /// Example:
        /// Snapshot name = 'NumberAdditionTest'
        /// Snapshot name extension = '5', '6', 'Result', '11'
        /// Result: 'NumberAdditionTest_5_6_Result_11'
        /// </param>
        /// <returns>The full name of a snapshot.</returns>
        public static SnapshotFullName FullName(
            string snapshotName, SnapshotNameExtension snapshotNameExtension)
        {
            SnapshotFullName fullName = _snapshotName.Value;

            if (fullName is null)
            {
                fullName = Snapshooter.ResolveSnapshotFullName(snapshotName, snapshotNameExtension);
                _snapshotName.Value = fullName;
            }

            return fullName;
        }

        private static Snapshooter Snapshooter
        {
            get
            {
                var snapshotSerializer =
                    new SnapshotSerializer(new GlobalSnapshotSettingsResolver());

                return
                    new Snapshooter(
                        new SnapshotAssert(
                            snapshotSerializer,
                            new SnapshotFileHandler(),
                            new SnapshotEnvironmentCleaner(
                                new SnapshotFileHandler()),
                            new JsonSnapshotComparer(
                                new MSTestAssert(),
                                snapshotSerializer),
                            new JsonSnapshotFormatter(snapshotSerializer)),
                        new SnapshotFullNameResolver(
                            new MSTestSnapshotFullNameReader()));
            }
        }
    }
}

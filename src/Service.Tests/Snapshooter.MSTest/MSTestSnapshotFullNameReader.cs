using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Snapshooter.Core;
using Snapshooter.Exceptions;
using Snapshooter.Extensions;

namespace Snapshooter.MSTest
{
    /// <summary>
    /// A mstest snapshot full name reader is responsible to get the information  
    /// for the snapshot file from a mstest test.
    /// </summary>
    public class MSTestSnapshotFullNameReader : ISnapshotFullNameReader
    {
        private static readonly Dictionary<string, int> _dataTestMethodRowIndex = new();

        /// <summary>
        /// Evaluates the snapshot full name information.
        /// </summary>
        /// <returns>The full name of the snapshot.</returns>
        public SnapshotFullName ReadSnapshotFullName()
        {
            SnapshotFullName snapshotFullName = null;
            StackFrame[] stackFrames = new StackTrace(true).GetFrames();
            foreach (StackFrame stackFrame in stackFrames)
            {
                MethodBase method = stackFrame.GetMethod();
                if (IsMSTestTest(method))
                {
                    snapshotFullName = new SnapshotFullName(
                        GetMethodSnapshotName(method),
                        Path.GetDirectoryName(stackFrame.GetFileName()));

                    break;
                }

                MethodBase asyncMethod = EvaluateAsynchronMethodBase(method);
                if (IsMSTestTest(asyncMethod))
                {
                    snapshotFullName = new SnapshotFullName(
                        GetMethodSnapshotName(asyncMethod),
                        Path.GetDirectoryName(stackFrame.GetFileName()));

                    break;
                }
            }

            if (snapshotFullName == null)
            {
                throw new SnapshotTestException(
                    "The snapshot full name could not be evaluated. " +
                    "This error can occur, if you use the snapshot match " +
                    "within a async test helper child method. To solve this issue, " +
                    "use the Snapshot.FullName directly in the unit test to " +
                    "get the snapshot name, then reach this name to your " +
                    "Snapshot.Match method.");
            }

            snapshotFullName = LiveUnitTestingDirectoryResolver
                                    .CheckForSession(snapshotFullName);

            return snapshotFullName;
        }

        private static bool IsMSTestTest(MemberInfo method)
        {
            bool isFactTest = IsTestMethodTestMethod(method);
            bool isTheoryTest = IsDataTestMethodTestMethod(method);

            return isFactTest || isTheoryTest;
        }

        private static bool IsTestMethodTestMethod(MemberInfo method)
        {
            return method?.GetCustomAttributes(typeof(TestMethodAttribute))?.Any() ?? false;
        }

        private static bool IsDataTestMethodTestMethod(MemberInfo method)
        {
            return method?.GetCustomAttributes(typeof(DataTestMethodAttribute))?.Any() ?? false;
        }

        private static MethodBase EvaluateAsynchronMethodBase(MemberInfo method)
        {
            Type methodDeclaringType = method?.DeclaringType;
            Type classDeclaringType = methodDeclaringType?.DeclaringType;

            MethodInfo actualMethodInfo = null;
            if (classDeclaringType != null)
            {
                IEnumerable<MethodInfo> selectedMethodInfos =
                from methodInfo in classDeclaringType.GetMethods()
                let stateMachineAttribute = methodInfo
                    .GetCustomAttribute<AsyncStateMachineAttribute>()
                where stateMachineAttribute != null &&
                    stateMachineAttribute.StateMachineType == methodDeclaringType
                select methodInfo;

                actualMethodInfo = selectedMethodInfos.SingleOrDefault();
            }

            return actualMethodInfo;
        }

        private static string GetMethodSnapshotName(MethodBase method)
        {
            if (!IsDataTestMethodTestMethod(method))
            {
                return method.ToName();
            }

            if (!_dataTestMethodRowIndex.ContainsKey(method.Name))
            {
                _dataTestMethodRowIndex[method.Name] = 0;
            }
            else
            {
                _dataTestMethodRowIndex[method.Name] += 1;
            }

            IEnumerable<DataRowAttribute> dataRowAttributes = method.GetCustomAttributes<DataRowAttribute>();
            DataRowAttribute currentRow = dataRowAttributes.ElementAt(_dataTestMethodRowIndex[method.Name]);

            return $"{method.DeclaringType.Name}." +
                method.Name +
                $"_{string.Join("_", currentRow.Data.Select(d => d.ToString()))}";
        }
    }
}

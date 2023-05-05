// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;

namespace Azure.DataApiBuilder.Service.Services
{
    /// <summary>
    /// Helper class used to resolve the underlying DbType for the parameter for its given SystemType. 
    /// </summary>
    public static class DbTypeHelper
    {
        private static Dictionary<Type, DbType> _systemTypeToDbTypeMap = new()
        {
            [typeof(byte)] = DbType.Byte,
            [typeof(sbyte)] = DbType.SByte,
            [typeof(short)] = DbType.Int16,
            [typeof(ushort)] = DbType.UInt16,
            [typeof(int)] = DbType.Int32,
            [typeof(uint)] = DbType.UInt32,
            [typeof(long)] = DbType.Int64,
            [typeof(ulong)] = DbType.UInt64,
            [typeof(float)] = DbType.Single,
            [typeof(double)] = DbType.Double,
            [typeof(decimal)] = DbType.Decimal,
            [typeof(bool)] = DbType.Boolean,
            [typeof(string)] = DbType.String,
            [typeof(char)] = DbType.StringFixedLength,
            [typeof(Guid)] = DbType.Guid,
            [typeof(byte[])] = DbType.Binary,
            [typeof(TimeSpan)] = DbType.Time,
            [typeof(byte?)] = DbType.Byte,
            [typeof(sbyte?)] = DbType.SByte,
            [typeof(short?)] = DbType.Int16,
            [typeof(ushort?)] = DbType.UInt16,
            [typeof(int?)] = DbType.Int32,
            [typeof(uint?)] = DbType.UInt32,
            [typeof(long?)] = DbType.Int64,
            [typeof(ulong?)] = DbType.UInt64,
            [typeof(float?)] = DbType.Single,
            [typeof(double?)] = DbType.Double,
            [typeof(decimal?)] = DbType.Decimal,
            [typeof(bool?)] = DbType.Boolean,
            [typeof(char?)] = DbType.StringFixedLength,
            [typeof(Guid?)] = DbType.Guid,
            [typeof(TimeSpan?)] = DbType.Time,
            [typeof(object)] = DbType.Object
        };

        /// <summary>
        /// Returns the DbType for given system type.
        /// </summary>
        /// <param name="systemType">The system type for which the DbType is to be determined.</param>
        /// <returns>DbType for the given system type.</returns>
        public static DbType? GetDbTypeFromSystemType(Type systemType)
        {
            if (!_systemTypeToDbTypeMap.TryGetValue(systemType, out DbType dbType))
            {
                return null;
            }

            return dbType;
        }
    }
}

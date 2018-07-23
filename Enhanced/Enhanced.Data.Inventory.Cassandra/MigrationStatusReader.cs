/// <license>
///     Copyright (c) Contributors, InWorldz Halcyon Developers
///     See CONTRIBUTORS.TXT for a full list of copyright holders.
///     For an explanation of the license of each contributor and the content it 
///     covers please see the Licenses directory.
/// 
///     Redistribution and use in source and binary forms, with or without
///     modification, are permitted provided that the following conditions are met:
///         * Redistributions of source code must retain the above copyright
///         notice, this list of conditions and the following disclaimer.
///         * Redistributions in binary form must reproduce the above copyright
///         notice, this list of conditions and the following disclaimer in the
///         documentation and/or other materials provided with the distribution.
///         * Neither the name of the Halcyon Project nor the
///         names of its contributors may be used to endorse or promote products
///         derived from this software without specific prior written permission.
/// 
///     THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
///     EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
///     WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
///     DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
///     DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
///     (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
///     LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
///     ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
///     (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
///     SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
/// </license>

using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using OpenMetaverse;
using OpenSim.Data.SimpleDB;

namespace Enhanced.Data.Inventory.Cassandra
{
    /// <summary>
    ///     Checks the central database for the inventory migration status of the given user
    /// </summary>
    internal class MigrationStatusReader
    {
        private string _coreConnStr;
        private ConnectionFactory _connFactory;

        public MigrationStatusReader(string coreConnStr)
        {
            _coreConnStr = coreConnStr;
            _connFactory = new ConnectionFactory("MySQL", _coreConnStr);
        }

        public MigrationStatus GetUserMigrationStatus(UUID userId)
        {
            using (ISimpleDB conn = _connFactory.GetConnection())
            {
                const string query = "SELECT status FROM InventoryMigrationStatus WHERE user_id = ?userId";

                Dictionary<string, object> parms = new Dictionary<string, object>();
                parms.Add("?userId", userId);

                using (IDataReader reader = conn.QueryAndUseReader(query, parms))
                {
                    if (reader.Read())
                    {
                        short status = Convert.ToInt16(reader["status"]);
                        MigrationStatus retStatus = (MigrationStatus)status;
                        return retStatus;
                    }
                    else
                    {
                        return MigrationStatus.Unmigrated;
                    }
                }
            }
        }
    }
}
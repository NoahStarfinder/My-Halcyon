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
using System.Linq;
using System.Text;
using Aquiles.Cassandra10;
using OpenMetaverse;
using OpenSim.Data;
using OpenSim.Framework;
using OpenSim.Framework.Communications;

namespace Enhanced.Data.Inventory.Cassandra
{
    public class CassandraInventoryPlugin : IInventoryStoragePlugin
    {
        private InventoryStorage _storage;
        private LegacyMysqlInventoryStorage _legacyStorage;
        private DelayedMutationManager _delayedMutationMgr;
        private CassandraMigrationProviderSelector _storageSelector;

        #region IInventoryStoragePlugin Members

        public void Initialize(ConfigSettings settings)
        {
            AquilesHelper.Initialize();
            _storage = new InventoryStorage(settings.InventoryCluster);

            _delayedMutationMgr = new DelayedMutationManager();
            _delayedMutationMgr.Start();
            _storage.DelayedMutationMgr = _delayedMutationMgr;

            if (settings.InventoryMigrationActive)
            {
                _legacyStorage = new LegacyMysqlInventoryStorage(settings.LegacyInventorySource);
            }

            _storageSelector = new CassandraMigrationProviderSelector(settings.InventoryMigrationActive, settings.CoreConnectionString,
                _storage, _legacyStorage);

            ProviderRegistry.Instance.RegisterInterface<IInventoryProviderSelector>(_storageSelector);
        }

        public IInventoryStorage GetStorage()
        {
            return _storage;
        }

        #endregion

        #region IPlugin Members

        public string Version
        {
            get { return "1.0.0"; }
        }

        public string Name
        {
            get { return "Enhanced.Data.Inventory.Cassandra";  }
        }

        public void Initialize()
        {
            
        }

        #endregion

        #region IDisposable Members

        public void Dispose()
        {
            
        }

        #endregion
    }
}
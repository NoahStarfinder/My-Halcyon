/// <license>
///     Copyright (c) Contributors, My-Halcyon Developers
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
using System.IO;
using System.Linq;
using System.Text;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.StructuredData;
using OpenSim.Framework;
using OpenSim.Framework.Servers;
using OpenSim.Framework.Servers.HttpServer;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;

namespace Enhanced.ApplicationPlugins.AvatarRemoteCommandModule
{
    /// <summary>
    /// Sets up a remote HTTP POST url (on "http://regionIP:regionPort/avatarremotecommand")
    /// to take in avatar commands such as the AvatarChatCommand and does avatar-related commands
    /// in-world for the user
    /// 
    /// The base command requires:
    /// 
    /// Command [ app -> regionhost ] (HTTP POST)
    ///
    /// {
    ///      “id”:”[command_id]”
    ///      “userId”:”[user_uuid]”
    ///      “sessionId”:”[session_uuid]”
    ///      ”regionId”:”[region_uuid]”
    ///    }
    ///    
    /// <param name="id">the command type (RemoteCommandType)</param>
    /// <param name="userId">the user’s ID</param>
    /// <param name="sessionId">the user's current grid session id to verify this is a legitimate message</param>
    /// <param name="regionId">the user’s current regionID</param>
    ///    
    /// More info can be added on, as shown below for the AvatarChatCommand.
    /// 
    /// The response to this request will be in the following format:
    /// 
    /// CommandReply [ regionhost -> app ] (REPLY TO HTTP POST)
    ///
    /// {
    ///      "status":"OK|MOVED|NOTFOUND|UNAUTHORIZED"
    ///      "data":"[new_region_hostname/ip]"
    ///      "newRegionId":"[new region id if set]"
    /// }
    ///
    /// <param name="status">The status of the command (RemoteCommandResponse)</param>
    /// <param name="data">The data that will be returned (generally either the new regionURI if the user has moved, or the exception if the command returned ERROR)</param>
    /// </summary>
    /// <example>
    /// AvatarChatCommand will say text for a user inworld
    /// 
    /// AvatarChatCommand [ app -> regionhost ] (HTTP POST)
    ///
    /// {
    ///      “id”:”[command_id]”
    ///      “userId”:”[user_uuid]”
    ///      “sessionId”:”[session_uuid]”
    ///      ”regionId”:”[region_uuid]”
    ///   “channel”:”[chat_channel]”
    ///      “text”:”[text to be said]”
    /// } 
    /// 
    /// <param name="chat_channel">the channel number to say the chat on</param>
    /// <param name="text">the text to say in chat on the given channel</param>
    /// </example>
    /// <remarks>
    /// To enable this module, add
    /// [AvatarRemoteCommands]
    ///     Enabled = true
    ///     
    /// to your Halcyon.ini file
    /// </remarks>
    public class AvatarRemoteCommandModule : ISharedRegionModule
    {
        #region Declares

        private bool _enabled = false;
        private ExpiringCache<UUID, LeavingRegionInfo> _avatarRegionCache = new ExpiringCache<UUID, LeavingRegionInfo>();

        /// <summary>
        ///     The cache expiration time.
        /// </summary>
        private const double CACHE_EXPIRATION_TIME = 300;

        private List<Scene> _scenes = new List<Scene>();

        #endregion

        #region Enums and Classes

        /// <summary>
        ///     The command that will be fired
        /// </summary>
        public enum RemoteCommandType
        {
            /// <summary>
            ///     The avatar chat command.
			/// 
			///     Sends a chat message to the user
            /// </summary>
            AvatarChatCommand = 1,
				
            /// <summary>
            ///     The avatar teleport command.
			/// 
			///     Teleports the user to a given destination
            /// </summary>
            AvatarTeleportCommand = 2,
        }

        /// <summary>
        ///     The response that will be returned from a command
        /// </summary>
        public enum RemoteCommandResponse
        {
            /// <summary>
            ///     The command worked properly
            /// </summary>
            OK = 1,

            /// <summary>
            ///     The user is now in another sim
            /// </summary>
            MOVED = 2,

            /// <summary>
            ///     The user is not able to be located
            /// </summary>
            NOTFOUND = 3,

            /// <summary>
            ///     The user is not properly authenticated,
			///     and must request re-request authentication
			///     from the auth server
            /// </summary>
            UNAUTHORIZED = 4,

            /// <summary>
            ///     The command sent is invalid
            /// </summary>
            INVALID = 5,

            /// <summary>
            ///     An error occured while processing the command
            /// </summary>
            ERROR = 6
        }

        private class LeavingRegionInfo
        {
            public string RegionServerURI;
            public UUID SessionID;
        }

        #endregion

        #region INonSharedRegionModule members

        public void Initialize(IConfigSource source)
        {
            IConfig config = source.Configs["AvatarRemoteCommands"];
            
			if (config == null || !config.GetBoolean("Enabled", false))
			{
				return;
			}

            _enabled = true;
        }

        public void PostInitialize()
        {
        }

        public void AddRegion(Scene scene)
        {
            if (!_enabled)
			{
				return;
			}

            _scenes.Add(scene);

            scene.EventManager.OnAvatarLeavingRegion += EventManager_OnAvatarLeavingRegion;

            IHttpServer server = MainServer.GetHttpServer(scene.RegionInfo.HttpPort);
            server.AddStreamHandler(new BinaryStreamHandler("POST", "/avatarremotecommand", IncomingCommand));
        }

        public void RegionLoaded(Scene scene)
        {
        }

        public void RemoveRegion(Scene scene)
        {
            if (!_enabled)
			{
				return;
			}

            _scenes.Remove(scene);

            scene.EventManager.OnAvatarLeavingRegion -= EventManager_OnAvatarLeavingRegion;
            IHttpServer server = MainServer.GetHttpServer(scene.RegionInfo.HttpPort);
            server.RemoveStreamHandler("POST", "/avatarremotecommand");

        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "AvatarRemoteCommandModule"; }
        }

        public Type ReplaceableInterface
        {
            get { return null; }
        }

        #endregion

        #region Client events

        private void EventManager_OnAvatarLeavingRegion(ScenePresence presence, SimpleRegionInfo newRegion)
        {
			/// <summary>
			///     Add them to the cache
			/// </summary>
            LeavingRegionInfo info = new LeavingRegionInfo() { RegionServerURI = newRegion.InsecurePublicHTTPServerURI, SessionID = presence.ControllingClient.SessionId };
            _avatarRegionCache.AddOrUpdate(presence.UUID, info, CACHE_EXPIRATION_TIME);
        }

        #endregion

        #region HTTP processing

        private string IncomingCommand(byte[] data, string path, string param)
        {
            using (MemoryStream str = new MemoryStream(data))
            {
                OSDMap map = (OSDMap)OSDParser.DeserializeJson(str);

                UUID sessionID = map["sessionId"].AsUUID();
                UUID userID = map["userId"].AsUUID();
                UUID regionID = map["regionId"].AsUUID();

                Scene scene = _scenes.FirstOrDefault((s) => s.RegionInfo.RegionID == regionID);
                
                /// <summary>
				///     No scene found
				/// </summary>
				if (scene == null)
				{
					return BuildCommandResponse(RemoteCommandResponse.NOTFOUND, null);
				}

                if (!scene.ConnectionManager.IsAuthorized(userID, sessionID))
                {
					/// <summary>
					///     THey might not be in the scene.
					/// 
					///     So we check if they have left recently
					/// </summary>
                    LeavingRegionInfo info = null;
                    
					if (!_avatarRegionCache.TryGetValue(userID, out info) || info.SessionID != sessionID)
					{
						/// <summary>
						///     Wrong sessionID or was never here
						/// </summary>
						return BuildCommandResponse(RemoteCommandResponse.UNAUTHORIZED, null);
					}

                    /// <summary>
					///     They have moved out of this region
					/// </summary>
                    return BuildMovedCommandResponse(userID);
                }

                ScenePresence presence = scene.GetScenePresence(userID);
                
                /// <summary>
				///     Make sure that they are actually in the region
				/// </summary>
				if (presence == null || presence.IsChildAgent)
				{
					return BuildMovedCommandResponse(userID);
				}

                /// <summary>
				///     Process the command
				/// </summary>
                RemoteCommandType commandID = (RemoteCommandType)map["id"].AsInteger();
                
				switch (commandID)
                {
                    case RemoteCommandType.AvatarChatCommand:
						return ProcessAvatarChatCommand(presence, map);
                    case RemoteCommandType.AvatarTeleportCommand:
                        return ProcessAvatarTeleportCommand(presence, map);
                }
            }

            return BuildCommandResponse(RemoteCommandResponse.INVALID, null);
        }

        private string BuildMovedCommandResponse(UUID userID)
        {
            LeavingRegionInfo info;
          
			if (_avatarRegionCache.TryGetValue(userID, out info))
			{
				return BuildCommandResponse(RemoteCommandResponse.MOVED, info.RegionServerURI);
			}
            else
			{
				/// <summary>
				///     We do not know where they are
				/// </summary>
				return BuildCommandResponse(RemoteCommandResponse.NOTFOUND, null);
			}
        }

        private string BuildCommandResponse(RemoteCommandResponse rep, string data)
        {
            return this.BuildCommandResponse(rep, data, UUID.Zero);
        }

        private string BuildCommandResponse(RemoteCommandResponse rep, string data, UUID newRegionId)
        {
            OSDMap map = new OSDMap();
            map["status"] = (int)rep;
            
			if (!string.IsNullOrEmpty(data))
			{
				map["data"] = data;
			}
            
			if (newRegionId != UUID.Zero)
			{
				map["newRegionID"] = newRegionId.ToString();
			}

            return OSDParser.SerializeJsonString(map);
        }

        #endregion

        #region Command processing

        private string ProcessAvatarChatCommand(ScenePresence presence, OSDMap map)
        {
            IChatModule chatModule = presence.Scene.RequestModuleInterface<IChatModule>();
          
			if (chatModule == null)
			{
				return BuildCommandResponse(RemoteCommandResponse.ERROR, "No chat module found");
			}

            OSDMap dataMap = (OSDMap)map["data"];

            int channel = dataMap["channel"].AsInteger();
            string message = dataMap["text"].AsString();

            /// <summary>
			///     Send the message
			/// </summary>
            chatModule.OnChatFromClient(presence.ControllingClient, new OSChatMessage
            {
                Channel = channel,
                DestinationUUID = UUID.Zero,
                From = String.Empty,
                Message = message,
                Position = presence.AbsolutePosition,
                Scene = presence.Scene,
                SenderUUID = presence.UUID,
                Type = ChatTypeEnum.Say
            });

            return BuildCommandResponse(RemoteCommandResponse.OK, null);
        }

        private string ProcessAvatarTeleportCommand(ScenePresence presence, OSDMap map)
        {
            OSDMap dataMap = (OSDMap)map["data"];

            string regionName = dataMap["regionName"].AsString();
            Vector3 pos = dataMap["pos"].AsVector3();
            Vector3 lookAt = new Vector3(128f, 128f, pos.Z);

            if (dataMap.ContainsKey("lookAt"))
            {
                lookAt = dataMap["lookAt"].AsVector3();
            }

            /// <summary>
			///     IS this a different region?
			/// </summary>
            if (regionName != presence.Scene.RegionInfo.RegionName)
			{
				presence.ControllingCLient.SendTeleportLocationStart();
			}

            presence.Scene.RequestTeleportLocation(presence.ControllingClient,
                regionName, pos, lookAt, (uint)TeleportFlags.ViaLocation);

            return BuildCommandResponse(RemoteCommandResponse.OK, null);
        }

        #endregion
    }
}
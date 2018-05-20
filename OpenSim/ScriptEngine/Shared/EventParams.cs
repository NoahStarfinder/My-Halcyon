/*
 * Copyright (c) InWorldz Halcyon Developers
 * Copyright (c) Contributors, http://opensimulator.org/
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyright
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Collections.Generic;
using System.Text;
using OpenMetaverse;
using OpenSim.Region.ScriptEngine.Shared;
using log4net;
using System.Reflection;

namespace OpenSim.ScriptEngine.Shared
{
    /// <summary>
    /// Holds all the data required to execute a scripting event.
    /// </summary>
    public class EventParams
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        public string EventName;
        public Object[] Params;
        public Region.ScriptEngine.Shared.DetectParams[] DetectParams;
        public uint LocalID;
        public UUID ItemID;

        public EventParams(uint localID, UUID itemID, string eventName, Object[] eventParams, DetectParams[] detectParams)
        {
            LocalID = localID;
            ItemID = itemID;
            EventName = eventName;
            Params = eventParams;
            DetectParams = detectParams;
        }
        public EventParams(uint localID, string eventName, Object[] eventParams, DetectParams[] detectParams)
        {
            LocalID = localID;
            EventName = eventName;
            Params = eventParams;
            DetectParams = detectParams;
        }
        public void test(params object[] args)
        {
            string functionName = "test";
            test2(functionName, args);
        }
        public void test2(string functionName, params object[] args)
        {
            String logMessage = functionName;
            foreach (object arg in args)
            {
                logMessage +=", "+arg;
            }
            m_log.Debug(logMessage);
        }


    }
}
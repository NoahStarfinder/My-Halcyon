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
 *     * Neither the name of the OpenSim Project nor the
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
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;
using log4net;
using Nini.Config;
using OpenMetaverse;
using OpenMetaverse.Imaging;
using OpenSim.Region.Framework.Interfaces;
using OpenSim.Region.Framework.Scenes;
using OpenSim.Region.Interfaces;

namespace OpenSim.Region.CoreModules.World.WorldMap
{
    public enum DrawRoutine
    {
        Rectangle,
        Polygon,
        Ellipse
    }

    public struct face
    {
        public Point[] pts;
    }

    public struct DrawStruct
    {
        public SolidBrush brush;
        public face[] trns;
    }

    public struct DrawStruct2
    {
        public float sort_order;
        public SolidBrush brush;
        public Point[] vertices;
    }

    /// <summary>
    ///     A very fast, but special purpose, 
    ///     tool for doing lots of drawing operations.
    /// </summary>
    public class DirectBitmap : IDisposable
    {
        public Bitmap Bitmap { get; private set; }
        public Int32[] Bits { get; private set; }
        public bool Disposed { get; private set; }
        public int Height { get; private set; }
        public int Width { get; private set; }

        protected GCHandle BitsHandle { get; private set; }

        public DirectBitmap(int width, int height)
        {
            Width = width;
            Height = height;
            Bits = new Int32[width * height];
            BitsHandle = GCHandle.Alloc(Bits, GCHandleType.Pinned);
            Bitmap = new Bitmap(width, height, width * 4, PixelFormat.Format32bppPArgb, BitsHandle.AddrOfPinnedObject());
        }

        public void Dispose()
        {
            if (Disposed)
            {
                return;
            }

            Disposed = true;
            Bitmap.Dispose();
            BitsHandle.Free();
        }
    }

    public class MapImageModule : IMapImageGenerator, IRegionModule
    {
        private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private Scene m_scene;
        private IConfigSource m_config;
        private IMapTileTerrainRenderer terrainRenderer;

        private bool drawPrimVolume = true;
        private bool textureTerrain = false;

        private byte[] imageData = null;

        private DirectBitmap mapbmp = new DirectBitmap(256, 256);

        #region IMapImageGenerator Members

        public byte[] WriteJpeg2000Image()
        {
            if (terrainRenderer != null)
            {
                terrainRenderer.TerrainToBitmap(mapbmp);
            }

            if (drawPrimVolume)
            {
                DrawObjectVolume(m_scene, mapbmp);
            }

            int t = Environment.TickCount;

            try
            {
                imageData = OpenJPEG.EncodeFromImage(mapbmp.Bitmap, true);
            }
            catch (Exception e) // LEGIT: Catching problems caused by OpenJPEG p/invoke
            {
                m_log.Error("[Map Tile]: Failed generating terrain map: " + e);
            }

            t = Environment.TickCount - t;
            m_log.InfoFormat("[Map Tile]: encoding of image needed {0}ms", t);

            return imageData;
        }

        #endregion

        #region IRegionModule Members

        public void Initialize(Scene scene, IConfigSource source)
        {
            m_scene = scene;
            m_config = source;

            try
            {
                IConfig startupConfig = m_config.Configs["Startup"]; // Location supported for legacy INI files.
                IConfig worldmapConfig = m_config.Configs["WorldMap"];

                if (startupConfig.GetString("MapImageModule", "MapImageModule") != "MapImageModule")
                {
                    return;
                }

                // Go find the parameters in the new location and if not found go looking in the old.
                drawPrimVolume = (worldmapConfig != null && worldmapConfig.Contains("DrawPrimOnMapTile")) ?
                    worldmapConfig.GetBoolean("DrawPrimOnMapTile", drawPrimVolume) :
                    startupConfig.GetBoolean("DrawPrimOnMapTile", drawPrimVolume);

                textureTerrain = (worldmapConfig != null && worldmapConfig.Contains("TextureOnMapTile")) ?
                    worldmapConfig.GetBoolean("TextureOnMapTile", textureTerrain) :
                    startupConfig.GetBoolean("TextureOnMapTile", textureTerrain);
            }
            catch
            {
                m_log.Warn("[Map Tile]: Failed to load StartupConfig");
            }

            if (textureTerrain)
            {
                terrainRenderer = new TexturedMapTileRenderer();
            }
            else
            {
                terrainRenderer = new ShadedMapTileRenderer();
            }

            terrainRenderer.Initialize(m_scene, m_config);

            m_scene.RegisterModuleInterface<IMapImageGenerator>(this);
        }

        public void PostInitialize()
        {
        }

        public void Close()
        {
        }

        public string Name
        {
            get { return "MapImageModule"; }
        }

        public bool IsSharedModule
        {
            get { return false; }
        }

        #endregion

        private static readonly SolidBrush DefaultBrush = new SolidBrush(Color.Black);
        private static SolidBrush GetFaceBrush(SceneObjectPart part, uint face)
        {
            // Block sillyness that would cause an exception.
            if (face >= OpenMetaverse.Primitive.TextureEntry.MAX_FACES)
            {
                return DefaultBrush;
            }

            var facetexture = part.Shape.Textures.GetFace(face);
            // GetFace throws a generic exception if the parameter is greater than MAX_FACES.

            // TODO: compute a better color from the texture data AND the color applied.

            return new SolidBrush(Color.FromArgb(
                Math.Max(0, Math.Min(255, (int)(facetexture.RGBA.R * 255f))),
                Math.Max(0, Math.Min(255, (int)(facetexture.RGBA.G * 255f))),
                Math.Max(0, Math.Min(255, (int)(facetexture.RGBA.B * 255f)))
            ));
            // FromARGB can throw an exception if a parameter is outside 0-255, but that is prevented.
        }

        private static float ZOfCrossDiff(ref Vector3 P, ref Vector3 Q, ref Vector3 R)
        {
            // let A = Q - P
            // let B = R - P
            // Vz = AxBy - AyBx
            //    = (Qx - Px)(Ry - Py) - (Qy - Py)(Rx - Px)
            return (Q.X - P.X)* (R.Y - P.Y) - (Q.Y - P.Y) * (R.X - P.X);
        }

        private static DirectBitmap DrawObjectVolume(Scene whichScene, DirectBitmap mapbmp)
        {
            int time_start = Environment.TickCount;
            m_log.Info("[Map Tile]: Generating Maptile Step 2: Object Volume Profile");

            float scale_factor = (float)mapbmp.Height / OpenSim.Framework.Constants.RegionSize;

            SceneObjectGroup sog;
            Vector3 pos;
            Quaternion rot;

            Vector3 rotated_radial_scale;
            Vector3 radial_scale;

            DrawStruct2 drawdata;

            var drawdata_for_sorting = new List<DrawStruct2>();

            var vertices = new Vector3[8];

            // Get all the faces for valid prims and prep them for drawing.
            var entities = whichScene.GetEntities(); // GetEntities returns a new list of entities, so no threading issues.

            foreach (EntityBase obj in entities)
            {
                // Only SOGs till have the needed parts.
                sog = obj as SceneObjectGroup;

                if (sog == null)
                {
                    continue;
                }

                foreach (var part in sog.GetParts())
                {
                    // FILTERING PASS
                    if (
                        // get the null checks out of the way
                        part == null ||
                        part.Shape == null ||
                        part.Shape.Textures == null ||
                        part.Shape.Textures.DefaultTexture == null ||

                        // Make sure the object isn't temp or phys
                        (part.Flags & (PrimFlags.Physics | PrimFlags.Temporary | PrimFlags.TemporaryOnRez)) != 0 ||

                        // Draw only if the object is at least 1 meter wide in all directions
                        part.Scale.X <= 1f || part.Scale.Y <= 1f || part.Scale.Z <= 1f ||

                        // Eliminate trees from this since we don't really have a good tree representation
                        part.Shape.PCode == (byte)PCode.Tree || part.Shape.PCode == (byte)PCode.NewTree || part.Shape.PCode == (byte)PCode.Grass
                    )
                        continue;

                    pos = part.GetWorldPosition();

                    if (
                        // skip prim in non-finite position
                        Single.IsNaN(pos.X) || Single.IsNaN(pos.Y) ||
                        Single.IsInfinity(pos.X) || Single.IsInfinity(pos.Y) ||

                        // skip prim outside of region (REVISIT: prims can be outside of region and still overlap into the region.)
                        pos.X < 0f || pos.X >= 256f || pos.Y < 0f || pos.Y >= 256f ||

                        // skip prim Z at or above 256m above the terrain at that position.
                        pos.Z >= (whichScene.Heightmap.GetRawHeightAt((int)pos.X, (int)pos.Y) + 256f)
                    )
                        continue;

                    rot = part.GetWorldRotation();

                    radial_scale.X = part.Shape.Scale.X * 0.5f;
                    radial_scale.Y = part.Shape.Scale.Y * 0.5f;
                    radial_scale.Z = part.Shape.Scale.Z * 0.5f;

                    /// <summary>
                    ///     Vertex pattern:
                    ///         # XYZ
                    ///         0 --+
                    ///         1 +-+
                    ///         2 +++
                    ///         3 -++
                    ///         4 ---
                    ///         5 +--
                    ///         6 ++-
                    ///         7 -+-
                    /// </summary>
                    rotated_radial_scale.X = -radial_scale.X;
                    rotated_radial_scale.Y = -radial_scale.Y;
                    rotated_radial_scale.Z = radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[0].X = pos.X + rotated_radial_scale.X;
                    vertices[0].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[0].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = radial_scale.X;
                    rotated_radial_scale.Y = -radial_scale.Y;
                    rotated_radial_scale.Z = radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[1].X = pos.X + rotated_radial_scale.X;
                    vertices[1].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[1].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = radial_scale.X;
                    rotated_radial_scale.Y = radial_scale.Y;
                    rotated_radial_scale.Z = radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[2].X = pos.X + rotated_radial_scale.X;
                    vertices[2].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[2].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = -radial_scale.X;
                    rotated_radial_scale.Y = radial_scale.Y;
                    rotated_radial_scale.Z = radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[3].X = pos.X + rotated_radial_scale.X;
                    vertices[3].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[3].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = -radial_scale.X;
                    rotated_radial_scale.Y = -radial_scale.Y;
                    rotated_radial_scale.Z = -radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[4].X = pos.X + rotated_radial_scale.X;
                    vertices[4].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[4].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = radial_scale.X;
                    rotated_radial_scale.Y = -radial_scale.Y;
                    rotated_radial_scale.Z = -radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[5].X = pos.X + rotated_radial_scale.X;
                    vertices[5].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[5].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = radial_scale.X;
                    rotated_radial_scale.Y = radial_scale.Y;
                    rotated_radial_scale.Z = -radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[6].X = pos.X + rotated_radial_scale.X;
                    vertices[6].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[6].Z = pos.Z + rotated_radial_scale.Z;

                    rotated_radial_scale.X = -radial_scale.X;
                    rotated_radial_scale.Y = radial_scale.Y;
                    rotated_radial_scale.Z = -radial_scale.Z;
                    rotated_radial_scale *= rot;
                    vertices[7].X = pos.X + rotated_radial_scale.X;
                    vertices[7].Y = pos.Y + rotated_radial_scale.Y;
                    vertices[7].Z = pos.Z + rotated_radial_scale.Z;

                    // SORT HEIGHT CALC

                    // Sort faces by AABB top height, which by nature will always be the maximum Z value of all upwards facing face vertices, but is also simply the highest vertex.
                    drawdata.sort_order =
                        Math.Max(vertices[0].Z,
                            Math.Max(vertices[1].Z,
                                Math.Max(vertices[2].Z,
                                    Math.Max(vertices[3].Z,
                                        Math.Max(vertices[4].Z,
                                            Math.Max(vertices[5].Z,
                                                Math.Max(vertices[6].Z,
                                                    vertices[7].Z
                                                )
                                            )
                                        )
                                    )
                                )
                            )
                        );

                    // OBB DRAWING PREPARATION PASS
                    /* * * * * * * * * * * * * * * * * * */

                    // Compute face 0 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[0], ref vertices[1], ref vertices[3]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 0);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[0].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[0].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[1].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[1].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[2].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[2].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[3].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[3].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }

                    // Compute face 1 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[4], ref vertices[5], ref vertices[0]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 1);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[4].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[4].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[5].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[5].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[1].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[1].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[0].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[0].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }

                    // Compute face 2 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[5], ref vertices[6], ref vertices[1]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 2);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[5].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[5].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[6].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[6].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[2].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[2].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[1].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[1].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }

                    // Compute face 3 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[6], ref vertices[7], ref vertices[2]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 3);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[6].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[6].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[7].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[7].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[3].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[3].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[2].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[2].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }

                    // Compute face 4 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[7], ref vertices[4], ref vertices[3]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 4);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[7].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[7].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[4].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[4].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[0].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[0].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[3].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[3].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }

                    // Compute face 5 of OBB and add if facing up.
                    if (ZOfCrossDiff(ref vertices[7], ref vertices[6], ref vertices[4]) > 0)
                    {
                        drawdata.brush = GetFaceBrush(part, 5);

                        drawdata.vertices = new Point[4];
                        drawdata.vertices[0].X = (int)(vertices[7].X * scale_factor);
                        drawdata.vertices[0].Y = mapbmp.Height - (int)(vertices[7].Y * scale_factor);

                        drawdata.vertices[1].X = (int)(vertices[6].X * scale_factor);
                        drawdata.vertices[1].Y = mapbmp.Height - (int)(vertices[6].Y * scale_factor);

                        drawdata.vertices[2].X = (int)(vertices[5].X * scale_factor);
                        drawdata.vertices[2].Y = mapbmp.Height - (int)(vertices[5].Y * scale_factor);

                        drawdata.vertices[3].X = (int)(vertices[4].X * scale_factor);
                        drawdata.vertices[3].Y = mapbmp.Height - (int)(vertices[4].Y * scale_factor);

                        drawdata_for_sorting.Add(drawdata);
                    }
                }
            }

            // Sort faces by Z position
            drawdata_for_sorting.Sort((h1, h2) => h1.sort_order.CompareTo(h2.sort_order));;

            // Draw the faces
            using (Graphics g = Graphics.FromImage(mapbmp.Bitmap))
            {
                g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.None;
                g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;

                for (int s = 0; s < drawdata_for_sorting.Count; s++)
                {
                    g.FillPolygon(drawdata_for_sorting[s].brush, drawdata_for_sorting[s].vertices);
                }
            }

            m_log.InfoFormat("[Map Tile]: Generating Maptile Step 2 (Objects): Timing: " + "total time: {0}ms", Environment.TickCount - time_start);

            return mapbmp;
        }
    }
}
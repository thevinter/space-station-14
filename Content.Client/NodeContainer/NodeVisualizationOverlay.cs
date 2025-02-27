﻿using System;
using System.Collections.Generic;
using System.Text;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using static Content.Shared.NodeContainer.NodeVis;

namespace Content.Client.NodeContainer
{
    public sealed class NodeVisualizationOverlay : Overlay
    {
        private readonly NodeGroupSystem _system;
        private readonly IEntityLookup _lookup;
        private readonly IMapManager _mapManager;
        private readonly IInputManager _inputManager;
        private readonly IEyeManager _eyeManager;
        private readonly IEntityManager _entityManager;

        private readonly Dictionary<(int, int), NodeRenderData> _nodeIndex = new();
        private readonly Dictionary<GridId, Dictionary<Vector2i, List<(GroupData, NodeDatum)>>> _gridIndex = new ();

        private readonly Font _font;

        private (int group, int node)? _hovered;
        private float _time;

        public override OverlaySpace Space => OverlaySpace.ScreenSpace | OverlaySpace.WorldSpace;

        public NodeVisualizationOverlay(
            NodeGroupSystem system,
            IEntityLookup lookup,
            IMapManager mapManager,
            IInputManager inputManager,
            IEyeManager eyeManager,
            IResourceCache cache,
            IEntityManager entityManager)
        {
            _system = system;
            _lookup = lookup;
            _mapManager = mapManager;
            _inputManager = inputManager;
            _eyeManager = eyeManager;
            _entityManager = entityManager;

            _font = cache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 12);
        }

        protected override void Draw(in OverlayDrawArgs args)
        {
            if ((args.Space & OverlaySpace.WorldSpace) != 0)
            {
                DrawWorld(args);
            }
            else if ((args.Space & OverlaySpace.ScreenSpace) != 0)
            {
                DrawScreen(args);
            }
        }

        private void DrawScreen(in OverlayDrawArgs args)
        {
            if (_hovered == null)
                return;

            var (groupId, nodeId) = _hovered.Value;

            var group = _system.Groups[groupId];
            var node = _system.NodeLookup[(groupId, nodeId)];

            var mousePos = _inputManager.MouseScreenPosition.Position;

            var entity = _entityManager.GetEntity(node.Entity);

            var gridId = entity.Transform.GridID;
            var grid = _mapManager.GetGrid(gridId);
            var gridTile = grid.TileIndicesFor(entity.Transform.Coordinates);

            var sb = new StringBuilder();
            sb.Append($"entity: {entity}\n");
            sb.Append($"group id: {group.GroupId}\n");
            sb.Append($"node: {node.Name}\n");
            sb.Append($"type: {node.Type}\n");
            sb.Append($"grid pos: {gridTile}\n");

            args.ScreenHandle.DrawString(_font, mousePos + (20, -20), sb.ToString());
        }

        private void DrawWorld(in OverlayDrawArgs overlayDrawArgs)
        {
            const float nodeSize = 8f / 32;
            const float nodeOffset = 6f / 32;

            var handle = overlayDrawArgs.WorldHandle;

            var map = overlayDrawArgs.Viewport.Eye?.Position.MapId ?? default;
            if (map == MapId.Nullspace)
                return;

            var mouseScreenPos = _inputManager.MouseScreenPosition;
            var mouseWorldPos = _eyeManager.ScreenToMap(mouseScreenPos).Position;

            _hovered = default;

            var cursorBox = Box2.CenteredAround(mouseWorldPos, (nodeSize, nodeSize));

            // Group visible nodes by grid tiles.
            var worldAABB = overlayDrawArgs.WorldAABB;
            _lookup.FastEntitiesIntersecting(map, ref worldAABB, entity =>
            {
                if (!_system.Entities.TryGetValue(entity.Uid, out var nodeData))
                    return;

                var gridId = entity.Transform.GridID;
                var grid = _mapManager.GetGrid(gridId);
                var gridDict = _gridIndex.GetOrNew(gridId);
                var coords = entity.Transform.Coordinates;

                // TODO: This probably shouldn't be capable of returning NaN...
                if (float.IsNaN(coords.Position.X) || float.IsNaN(coords.Position.Y))
                    return;

                var tile = gridDict.GetOrNew(grid.TileIndicesFor(coords));

                foreach (var (group, nodeDatum) in nodeData)
                {
                    if (!_system.Filtered.Contains(group.GroupId))
                    {
                        tile.Add((group, nodeDatum));
                    }
                }
            });

            foreach (var (gridId, gridDict) in _gridIndex)
            {
                var grid = _mapManager.GetGrid(gridId);
                foreach (var (pos, list) in gridDict)
                {
                    var centerPos = (Vector2) pos + grid.TileSize / 2f;
                    list.Sort(NodeDisplayComparer.Instance);

                    var offset = -(list.Count - 1) * nodeOffset / 2;

                    foreach (var (group, node) in list)
                    {
                        var nodePos = centerPos + (offset, offset);
                        if (cursorBox.Contains(nodePos))
                            _hovered = (group.NetId, node.NetId);

                        _nodeIndex[(group.NetId, node.NetId)] = new NodeRenderData(group, node, nodePos);
                        offset += nodeOffset;
                    }
                }

                handle.SetTransform(grid.WorldMatrix);

                foreach (var nodeRenderData in _nodeIndex.Values)
                {
                    var pos = nodeRenderData.NodePos;
                    var bounds = Box2.CenteredAround(pos, (nodeSize, nodeSize));

                    var groupData = nodeRenderData.GroupData;
                    var color = groupData.Color;

                    if (!_hovered.HasValue)
                        color.A = 0.5f;
                    else if (_hovered.Value.group != groupData.NetId)
                        color.A = 0.2f;
                    else
                        color.A = 0.75f + MathF.Sin(_time * 4) * 0.25f;

                    handle.DrawRect(bounds, color);

                    foreach (var reachable in nodeRenderData.NodeDatum.Reachable)
                    {
                        if (_nodeIndex.TryGetValue((groupData.NetId, reachable), out var reachDat))
                        {
                            handle.DrawLine(pos, reachDat.NodePos, color);
                        }
                    }
                }

                _nodeIndex.Clear();
            }


            handle.SetTransform(Matrix3.Identity);
            _gridIndex.Clear();
        }

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            _time += args.DeltaSeconds;
        }

        private sealed class NodeDisplayComparer : IComparer<(GroupData, NodeDatum)>
        {
            public static readonly NodeDisplayComparer Instance = new();

            public int Compare((GroupData, NodeDatum) x, (GroupData, NodeDatum) y)
            {
                var (groupX, nodeX) = x;
                var (groupY, nodeY) = y;

                var cmp = groupX.NetId.CompareTo(groupY.NetId);
                if (cmp != 0)
                    return cmp;

                return nodeX.NetId.CompareTo(nodeY.NetId);
            }
        }

        private sealed class NodeRenderData
        {
            public GroupData GroupData;
            public NodeDatum NodeDatum;
            public Vector2 NodePos;

            public NodeRenderData(GroupData groupData, NodeDatum nodeDatum, Vector2 nodePos)
            {
                GroupData = groupData;
                NodeDatum = nodeDatum;
                NodePos = nodePos;
            }
        }
    }
}

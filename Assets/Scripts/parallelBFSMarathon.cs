using System;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using Weland;

public class parallelBFSMarathon : MonoBehaviour
{
    public struct EdgeMeta
    {
        public int connectedNodeId;
        public int nodeId;
    }

    public struct NodeMeta
    {
        public int edgeStartIndex;
        public int edgeCount;

        public int nodeId;
    }

    [BurstCompile]
    public struct BFSNodesJob : IJobParallelFor
    {
        [ReadOnly] public NativeList<NodeMeta> nodes;
        [ReadOnly] public NativeList<EdgeMeta> edges;
        [ReadOnly] public NativeList<NodeMeta> currentNodes;

        public NativeList<NodeMeta>.ParallelWriter nextNodes;
        public NativeParallelHashSet<int>.ParallelWriter visited;
        public NativeList<int>.ParallelWriter result;

        public void Execute(int index)
        {
            NodeMeta node = currentNodes[index];

            for (int i = node.edgeStartIndex; i < node.edgeStartIndex + node.edgeCount; i++)
            {
                EdgeMeta edge = edges[i];
                int nextId = edge.connectedNodeId;

                if (!visited.Add(nextId))
                {
                    continue;
                }
                    
                NodeMeta nextNode = nodes[nextId];

                nextNodes.AddNoResize(nextNode);
                result.AddNoResize(nextId);
            }
        }
    }

    public Level level;

    public string Name = "Two Hallways";

    public int LevelNumber;

    public int rootNode;

    public int jobCompleted;

    public NativeList<int> Owner;
    public NativeList<int> Adjacent;

    public NativeList<NodeMeta> nodesNative;
    public NativeList<EdgeMeta> edgesNative;

    public NativeList<NodeMeta> sideA;
    public NativeList<NodeMeta> sideB;

    public NativeParallelHashSet<int> visited;
    public NativeList<int> result;

    void Start()
    {
        Owner = new NativeList<int>(Allocator.Persistent);
        Adjacent = new NativeList<int>(Allocator.Persistent);

        edgesNative = new NativeList<EdgeMeta>(Allocator.Persistent);
        nodesNative = new NativeList<NodeMeta>(Allocator.Persistent);

        LoadLevel();
        GetLines();
        MakeNodesAndEdges();

        sideA = new NativeList<NodeMeta>(nodesNative.Length, Allocator.Persistent);
        sideB = new NativeList<NodeMeta>(nodesNative.Length, Allocator.Persistent);

        visited = new NativeParallelHashSet<int>(nodesNative.Length, Allocator.Persistent);
        result = new NativeList<int>(nodesNative.Length, Allocator.Persistent);

        RunBFS(nodesNative[rootNode]);

        foreach (var id in result)
        {
            Debug.Log("Visited: " + id);
        }
    }

    void RunBFS(NodeMeta startNode)
    {
        sideA.Clear();
        sideB.Clear();
        visited.Clear();
        result.Clear();

        jobCompleted = 0;

        sideA.Add(startNode);
        visited.Add(startNode.nodeId);
        result.Add(startNode.nodeId);

        NativeList<NodeMeta> current = sideA;
        NativeList<NodeMeta> next = sideB;

        while (current.Length > 0)
        {
            next.Clear();

            BFSNodesJob job = new BFSNodesJob
            {
                nodes = nodesNative,
                edges = edgesNative,
                currentNodes = current,
                nextNodes = next.AsParallelWriter(),
                visited = visited.AsParallelWriter(),
                result = result.AsParallelWriter()
            };

            job.Schedule(current.Length, 32).Complete();

            jobCompleted += 1;

            if (jobCompleted % 2 == 0)
            {
                current = sideA;
                next = sideB;
            }
            else
            {
                current = sideB;
                next = sideA;
            }
        }
    }

    public void GetLines()
    {
        for (int i = 0; i < level.Lines.Count; ++i)
        {
            Weland.Line line = level.Lines[i];

            if (line.ClockwisePolygonOwner != -1)
            {
                if (line.LowestAdjacentCeiling != line.HighestAdjacentFloor)
                {
                    if (level.Polygons[line.ClockwisePolygonOwner].CeilingHeight > line.HighestAdjacentFloor &&
                        level.Polygons[line.ClockwisePolygonOwner].FloorHeight < line.LowestAdjacentCeiling)
                    {
                        Owner.Add(line.ClockwisePolygonOwner);

                        if (line.CounterclockwisePolygonOwner != -1)
                        {
                            Adjacent.Add(line.CounterclockwisePolygonOwner);
                        }
                        else
                        {
                            Adjacent.Add(-1);
                        }
                    }
                }
            }

            if (line.CounterclockwisePolygonOwner != -1)
            {
                if (line.LowestAdjacentCeiling != line.HighestAdjacentFloor)
                {
                    if (level.Polygons[line.CounterclockwisePolygonOwner].CeilingHeight > line.HighestAdjacentFloor &&
                        level.Polygons[line.CounterclockwisePolygonOwner].FloorHeight < line.LowestAdjacentCeiling)
                    {
                        Owner.Add(line.CounterclockwisePolygonOwner);

                        if (line.ClockwisePolygonOwner != -1)
                        {
                            Adjacent.Add(line.ClockwisePolygonOwner);
                        }
                        else
                        {
                            Adjacent.Add(-1);
                        }
                    }
                }
            }
        }
    }

    public void MakeNodesAndEdges()
    {
        int edgeStart = 0;

        for (int a = 0; a < level.Polygons.Count; a++)
        {
            int edgeCount = 0;

            for (int b = 0; b < Owner.Length; b++)
            {
                if (Owner[b] != a || Adjacent[b] == -1)
                {
                    continue;
                }

                EdgeMeta edgeMeta = new EdgeMeta
                {
                    connectedNodeId = Adjacent[b],
                    nodeId = a
                };
                
                edgesNative.Add(edgeMeta);
                edgeCount += 1;
            }

            NodeMeta nodeMeta = new NodeMeta
            {
                edgeStartIndex = edgeStart,
                edgeCount = edgeCount,

                nodeId = a
            };

            nodesNative.Add(nodeMeta);
            edgeStart += edgeCount;
        }
    }

    public void LoadLevel()
    {
        MapFile map = new MapFile();

        level = new Level();

        try
        {
            // Change name to load a different map
            map.Load(Path.Combine(Application.streamingAssetsPath, Name + ".sceA"));
        }
        catch (Exception exit)
        {
            Debug.LogError("Failed to load Map: " + exit.Message);
        }
        try
        {
            // Change the map directory number if the map has more than one level 
            level.Load(map.Directory[LevelNumber]);
        }
        catch (Exception exit)
        {
            Debug.LogError("Failed to load level: " + exit.Message);
        }
    }

    void OnDestroy()
    {
        if (nodesNative.IsCreated)
        {
            nodesNative.Dispose();
        }
        if (edgesNative.IsCreated)
        {
            edgesNative.Dispose();
        }
        if (Owner.IsCreated)
        {
            Owner.Dispose();
        }
        if (Adjacent.IsCreated)
        {
            Adjacent.Dispose();
        }
        if (sideA.IsCreated)
        {
            sideA.Dispose();
        }
        if (sideB.IsCreated)
        {
            sideB.Dispose();
        }
        if (visited.IsCreated)
        {
            visited.Dispose();
        }
        if (result.IsCreated)
        {
            result.Dispose();
        }
    }
}

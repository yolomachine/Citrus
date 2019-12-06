using Lime.PolygonMesh.Topology;
using Lime.PolygonMesh.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Lime.Source.Widgets.PolygonMesh;
using OpenTK.Audio;
using HalfEdge = Lime.PolygonMesh.Topology.HalfEdgeTopology.HalfEdge;

namespace Lime.PolygonMesh
{
	internal class HalfEdgeTriangulator
	{
		public HalfEdgeTopology Topology { get; }

		private HashSet<(int, int)> constrainedEdges = new HashSet<(int, int)>();
		private readonly Random random = new Random();

		public HalfEdgeTriangulator(HalfEdgeTopology topology)
		{
			Topology = topology;
		}

		private List<HalfEdge> HalfEdges => Topology.HalfEdges;
		private List<Vertex> Vertices => Topology.Vertices;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private HalfEdge Next(HalfEdge he) => Topology.Next(he);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private int Next(int index) => Topology.Next(index);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private HalfEdge Prev(HalfEdge he) => Topology.Prev(he);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private int Prev(int index) => Topology.Prev(index);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private HalfEdge Twin(HalfEdge he) => HalfEdges[he.Twin];
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private HalfEdge Twin(int index) => Twin(HalfEdges[index]);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void MakeTwins(int lhs, int rhs) => Topology.MakeTwins(lhs, rhs);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void SetEdgeConstraint(int index, bool value) => Topology.SetEdgeConstraint(index, value);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveHalfEdge(int index) => Topology.RemoveHalfEdge(index);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void RemoveTriangle(int index) => Topology.RemoveTriangle(index);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void Untwin(int index) => Topology.Untwin(index);
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private void Connect(int v1, int v2, int v3) => Topology.Connect(v1, v2, v3);

		private bool TryGetTwin(HalfEdge edge, out HalfEdge twin)
		{
			if (edge.Twin != -1) {
				twin = HalfEdges[edge.Twin];
				return true;
			}
			twin = HalfEdgeTopology.DummyHalfEdge;
			return false;
		}

		private bool TryGetTwin(int index, out HalfEdge twin)
		{
			var edge = HalfEdges[index];
			if (edge.Twin != -1) {
				twin = HalfEdges[edge.Twin];
				return true;
			}
			twin = HalfEdgeTopology.DummyHalfEdge;
			return false;
		}

		public void AddVertex(int vi)
		{
			var vertex = Vertices[vi];
			var t = LocateClosestTriangle(vertex.Pos, out var isInside);
			if (isInside) {
				if (OnEdge(t, vi, out var edge)) {
					SplitEdge(edge, vi);
					InsertConstrainedEdges(constrainedEdges);
					Topology.Invalidate();
					return;
				}
				TriangulateStarShapedPolygon(GetContourPolygon( t, vertex), vi);
			} else {
				var q = TriangulateVisibleBoundary(GetVisibleBoundary(vertex.Pos, GetTriangulationBoundary(t)), vi);
				System.Diagnostics.Debug.Assert(q.Count > 0);
				RestoreDelaunayProperty(q);
				System.Diagnostics.Debug.Assert(FullCheck());
			}
			InsertConstrainedEdges(constrainedEdges);
			constrainedEdges.Clear();
		}

		private HalfEdge RandomEdge() =>
			HalfEdges[random.Next(0, HalfEdges.Count - 1)];

		public void RemoveVertex(int vi)
		{
			var vertex = Vertices[vi];
			var polygon = GetBoundaryPolygon(FindIncidentEdge(LocateTriangle(vertex.Pos), vi));
			RestoreDelaunayProperty(HalfEdges[Next(polygon.Last.Value)].Origin == vi ?
					RemovePolygon(polygon) :
					TriangulatePolygonByEarClipping(polygon));
		}

		public bool FullCheck()
		{
			return true;
			for (int i = 0; i < HalfEdges.Count; i += 3) {
				if (
					HalfEdges[i].Index == -1 || HalfEdges[i + 1].Index == -1 ||
					HalfEdges[i + 2].Index == -1
				) {
					continue;
				}
				int p = HalfEdges[i].Origin,
					q = HalfEdges[i + 1].Origin,
					r = HalfEdges[i + 2].Origin;
				var he = HalfEdges[i];
				for (int j = 0; j < Vertices.Count; j++) {
					if (j != p && j != q && j != r) {
						if (InCircumcircle(he, Vertices[j])) {
							return false;
						}
					}
				}
			}
			return true;
		}

		//TODO make OnTriangleEdge and OnEdge
		private bool OnEdge(HalfEdge triangle, int vi, out HalfEdge edge)
		{
			var v1 = Vertices[triangle.Origin].Pos;
			var v2 = Vertices[Next(triangle).Origin].Pos;
			var v3 = Vertices[Prev(triangle).Origin].Pos;
			var v = Vertices[vi].Pos;
			edge = triangle;
			var p = PolygonMeshUtils.PointProjectionToLine(v, v1, v2, out bool isInside);
			if (isInside && Math.Abs((p - v).SqrLength) < Mathf.ZeroTolerance) {
				return true;
			}
			p = PolygonMeshUtils.PointProjectionToLine(v, v2, v3, out isInside);
			if (isInside && Math.Abs((p - v).SqrLength) < Mathf.ZeroTolerance) {
				edge = Next(triangle);
				return true;
			}
			p = PolygonMeshUtils.PointProjectionToLine(v, v3, v1, out isInside);
			if (isInside && Math.Abs((p - v).SqrLength) < Mathf.ZeroTolerance) {
				edge = Prev(triangle);
				return true;
			}
			return false;
		}

		private bool AreOnOppositeSides(Vector2 s1, Vector2 s2, Vector2 p1, Vector2 p2)
		{
			return GeometricPredicates.ExactOrient2D(s1.X, s1.Y, s2.X, s2.Y, p1.X, p1.Y) *
			       GeometricPredicates.ExactOrient2D(s1.X, s1.Y, s2.X, s2.Y, p2.X, p2.Y) < 0;
		}

		private HalfEdge LocateTriangle(HalfEdge start, Vertex vertex, out bool inside)
		{
			var current = start;
			inside = true;
			do {
				var next = Next(current);
				Vector2 p1 = Vertices[current.Origin].Pos;
				var areOpposite = AreOnOppositeSides(p1, Vertices[next.Origin].Pos, Vertices[Next(next).Origin].Pos, vertex.Pos);
				inside &= !areOpposite;
				if (areOpposite && current.Twin != -1) {
					start = current = HalfEdges[current.Twin];
					inside = true;
				}
				current = Next(current);
			} while (current.Index != start.Index);
			return current;
		}

		private HalfEdge LocateClosestTriangle(Vector2 position, out bool isInside)
		{
			var closest = HalfEdgeTopology.DummyHalfEdge;
			var closestDistance = float.MaxValue;
			isInside = false;
			for (var i = 0; i < HalfEdges.Count; i += 3) {
				var he = HalfEdges[i];
				if (he.Index == -1) {
					continue;
				}
				var next = Next(he);
				var prev = Prev(he);
				var p1 = Vertices[he.Origin].Pos;
				var p2 = Vertices[next.Origin].Pos;
				var p3 = Vertices[prev.Origin].Pos;
				if (
					!AreOnOppositeSides(p1, p2, p3, position) && !AreOnOppositeSides(p2, p3, p1, position) &&
					!AreOnOppositeSides(p3, p1, p2, position)
				) {
					isInside = true;
					return he;
				}
				UpdateClosest(p1, p2, he);
				UpdateClosest(p2, p3, next);
				UpdateClosest(p3, p1, prev);
				void UpdateClosest(Vector2 start, Vector2 end, HalfEdge edge)
				{
					var d = PointToSegmentSqrDistance(start, end, position);
					if (d < closestDistance) {
						closestDistance = d;
						closest = edge;
					}
				}
			}
			return closest;
		}

		private HalfEdge LocateTriangle(Vector2 position)
		{
			for (var i = 0; i < HalfEdges.Count; i += 3) {
				var he = HalfEdges[i];
				if (he.Index == -1) {
					continue;
				}
				var next = Next(he);
				var prev = Prev(he);
				var p1 = Vertices[he.Origin].Pos;
				var p2 = Vertices[next.Origin].Pos;
				var p3 = Vertices[prev.Origin].Pos;
				if (
					!AreOnOppositeSides(p1, p2, p3, position) && !AreOnOppositeSides(p2, p3, p1, position) &&
					!AreOnOppositeSides(p3, p1, p2, position)
				) {
					return he;
				}
			}
			return HalfEdgeTopology.DummyHalfEdge;
		}

		private HalfEdge FindIncidentEdge(HalfEdge edge, int vi)
		{
			return edge.Origin == vi ? edge :
				(edge = Next(edge)).Origin == vi ? edge :
					 (edge = Next(edge)).Origin == vi ? edge : throw new InvalidOperationException();
		}

		private bool InCircumcircle(HalfEdge edge, Vertex vertex)
		{
			var pos = vertex.Pos;
			var v1 = Vertices[edge.Origin].Pos;
			var v2 = Vertices[Next(edge).Origin].Pos;
			var v3 = Vertices[Prev(edge).Origin].Pos;
			return GeometricPredicates.ExactInCircle(v1.X, v1.Y, v2.X, v2.Y, v3.X, v3.Y, pos.X, pos.Y) > 0;
		}

		private LinkedList<int> GetContourPolygon(HalfEdge start, Vertex vertex)
		{
			var polygon = new LinkedList<int>();
			polygon.AddLast(start.Index);
			polygon.AddLast(Next(start.Index));
			polygon.AddLast(Prev(start.Index));
			var current = polygon.First;
			while (current != null) {
				var edge = HalfEdges[current.Value];
				if (edge.Twin != -1) {
					var twin = HalfEdges[edge.Twin];
					if (InCircumcircle(HalfEdges[edge.Twin], vertex)) {
						polygon.AddAfter(current, Next(twin.Index));
						polygon.AddAfter(current.Next, Prev(twin.Index));
						RemoveHalfEdge(edge.Index);
						RemoveHalfEdge(twin.Index);
						KeepConstrainedEdges(edge.Index);
						KeepConstrainedEdges(twin.Index);
						var next = current.Next;
						polygon.Remove(current);
						current = next;
						continue;
					}
				}
				current = current.Next;
			}
			return polygon;
		}

		private LinkedList<int> GetBoundaryPolygon(HalfEdge incident)
		{
			var polygon = new LinkedList<int>();
			bool reverse = false;
			var current = incident;
			do {
				var i = current.Index;
				if (reverse) {
					polygon.AddFirst(Next(i));
				} else {
					polygon.AddLast(Next(i));
				}
				var next = HalfEdges[reverse ? i : Next(polygon.Last.Value)];
				if (next.Twin == -1) {
					if (reverse) {
						System.Diagnostics.Debug.Assert(next.Index != -1);
						polygon.AddFirst(next.Index);
						return polygon;
					}
					polygon.RemoveFirst();
					polygon.AddLast(next.Index);
					System.Diagnostics.Debug.Assert(next.Index != -1);
					reverse = true;
					current = incident;
					continue;
				}
				current = reverse ? HalfEdges[Next(next.Twin)] : HalfEdges[next.Twin];
			} while (polygon.First.Value != polygon.Last.Value || polygon.Count < 2);
			polygon.Remove(polygon.Last);
			return polygon;
		}

		private void Connect(HalfEdge he, int vi)
		{
			Connect(he.Origin, Next(he).Origin, vi);
			if (he.Twin != -1) {
				MakeTwins(he.Twin, HalfEdges.Count - 3);
			}
			SetEdgeConstraint(HalfEdges.Count - 3, he.Constrained);
		}

		private void Connect(HalfEdge he1, HalfEdge he2)
		{
			Connect(he1.Origin, he2.Origin, Next(he2).Origin);
			if (he1.Twin != -1) {
				MakeTwins(he1.Twin, HalfEdges.Count - 3);
			}
			if (he2.Twin != -1) {
				MakeTwins(he2.Twin, HalfEdges.Count - 2);
			}
			SetEdgeConstraint(HalfEdges.Count - 3, he1.Constrained);
			SetEdgeConstraint(HalfEdges.Count - 2, he2.Constrained);
		}

		private float Area(Vector2 v1, Vector2 v2, Vector2 v3) =>
			(v2.X - v1.X) * (v3.Y - v1.Y) - (v2.Y - v1.Y) * (v3.X - v1.X);

		private void KeepConstrainedEdges(int triangle)
		{
			KeepConstrainedEdge(triangle);
			KeepConstrainedEdge(Prev(triangle));
			KeepConstrainedEdge(Next(triangle));
		}

		private void KeepConstrainedEdge(int edge)
		{
			var he = HalfEdges[edge];
			if (he.Constrained) {
				constrainedEdges.Add((he.Origin, HalfEdges[Next(edge)].Origin));
			}
		}

		private void SplitEdge(HalfEdge edge, int splitPoint)
		{
			void IternalSplitEdge(HalfEdge e, int sp)
			{
				var next = Next(e);
				var prev = Prev(e);
				Connect(e.Origin, sp, prev.Origin);
				Connect(sp, next.Origin, prev.Origin);
				if (next.Twin != -1) {
					MakeTwins(HalfEdges.Count - 2, next.Twin);
					SetEdgeConstraint(HalfEdges.Count - 2, next.Constrained || HalfEdges[next.Twin].Constrained);
				}
				if (prev.Twin != -1) {
					MakeTwins(HalfEdges.Count - 4, prev.Twin);
					SetEdgeConstraint(HalfEdges.Count - 4, prev.Constrained || HalfEdges[prev.Twin].Constrained);
				}
				MakeTwins(HalfEdges.Count - 5, HalfEdges.Count - 1);
			}
			IternalSplitEdge(edge, splitPoint);
			var res = (HalfEdges.Count - 6, HalfEdges.Count - 3);
			if (edge.Twin != -1) {
				IternalSplitEdge(HalfEdges[edge.Twin], splitPoint);
				MakeTwins(res.Item2, HalfEdges.Count - 6);
				MakeTwins(res.Item1, HalfEdges.Count - 3);
				RemoveTriangle(edge.Twin);
			}
			SetEdgeConstraint(res.Item1, edge.Constrained);
			SetEdgeConstraint(res.Item2, edge.Constrained);
			RemoveTriangle(edge.Index);
		}

		private void TriangulateStarShapedPolygon(LinkedList<int> polygon, int vi)
		{
			var current = polygon.First;
			foreach (var side in polygon) {
				KeepConstrainedEdges(side);
			}
			while (current != null) {
				var edge = HalfEdges[current.Value];
				Connect(HalfEdges[current.Value], vi);
				if (current.Previous != null) {
					MakeTwins(HalfEdges.Count - 1, Next(current.Previous.Value));
				}
				RemoveHalfEdge(edge.Index);
				KeepConstrainedEdges(edge.Index);
				current.Value = HalfEdges.Count - 3;
				current = current.Next;
			}
			MakeTwins(Next(polygon.Last.Value), Prev(polygon.First.Value));
		}

		private Queue<int> TriangulatePolygonByEarClipping(LinkedList<int> polygon)
		{
			var queue = new Queue<int>();
			bool CanCreateTriangle(int cur, int next)
			{
				HalfEdge he1 = HalfEdges[cur];
				int o1 = he1.Origin,
					o2 = HalfEdges[next].Origin,
					o3 = HalfEdges[Next(next)].Origin;
				Vector2 v1 = Vertices[o1].Pos,
						v2 = Vertices[o2].Pos,
						v3 = Vertices[o3].Pos;
				if (Vector2.DotProduct(new Vector2(-(v2 - v1).Y, (v2 - v1).X), v3 - v2) < 0) {
					return false;
				}
				foreach (var i in polygon) {
					var j = HalfEdges[i].Origin;
					if (j != o1 && j != o2 && j != o3) {
						var v = Vertices[j].Pos;
						if (
							GeometricPredicates.AdaptiveOrient2D(v.X, v.Y, v1.X, v1.Y, v2.X, v2.Y) >= 0 &&
							GeometricPredicates.AdaptiveOrient2D(v.X, v.Y, v2.X, v2.Y, v3.X, v3.Y) >= 0 &&
							GeometricPredicates.AdaptiveOrient2D(v.X, v.Y, v3.X, v3.Y, v1.X, v1.Y) >= 0
						) {
							return false;
						}
					}
				}
				return true;
			}
			var current = polygon.First;
			foreach (var side in polygon) {
				KeepConstrainedEdges(side);
			}
			while (polygon.Count > 2) {
				var next = current.Next ?? polygon.First;
				if (CanCreateTriangle(current.Value, next.Value)) {
					var he1 = HalfEdges[current.Value];
					var he2 = HalfEdges[next.Value];
					he2.Index = next.Value;
					Connect(he1, he2);
					queue.Enqueue(HalfEdges.Count - 1);
					var twin = new HalfEdge(HalfEdges.Count, he1.Origin, HalfEdges.Count - 1) {
						Constrained = HalfEdges[HalfEdges.Count - 1].Constrained
					};
					polygon.AddAfter(next, HalfEdges.Count);
					HalfEdges.Add(twin);
					HalfEdges.Add(new HalfEdge(-1, Next(he2).Origin, -1));
					HalfEdges.Add(HalfEdgeTopology.DummyHalfEdge);
					RemoveTriangle(he1.Index);
					RemoveTriangle(he2.Index);
					polygon.Remove(next);
					polygon.Remove(current);
					current = polygon.First;
				} else {
					current = next;
				}
			}
			var last = HalfEdges[polygon.Last.Value];
			var lastOriginal = HalfEdges[HalfEdges[polygon.First.Value].Twin];
			HalfEdges[lastOriginal.Index] = lastOriginal;
			lastOriginal.Twin = last.Twin;
			if (last.Twin != -1) {
				MakeTwins(last.Twin, lastOriginal.Index);
				SetEdgeConstraint(last.Twin, lastOriginal.Constrained || HalfEdges[last.Twin].Constrained);
			}
			RemoveTriangle(last.Index);
			queue.Enqueue(lastOriginal.Index);
			HalfEdges[polygon.First.Value] = HalfEdgeTopology.DummyHalfEdge;
			return queue;
		}

		private void RestoreDelaunayProperty(Queue<int> queue)
		{
			while (queue.Count > 0) {
				var i = queue.Dequeue();
				var he = HalfEdges[i];
				for (int j = 0; j < 3; ++j) {
					if (
						!he.Constrained && he.Twin != -1 &&
						InCircumcircle(he, Vertices[Prev(HalfEdges[he.Twin]).Origin])
					) {
						Flip(he);
						queue.Enqueue(i);
						queue.Enqueue(HalfEdges[Next(i)].Twin);
						break;
					}
					i = Next(i);
					he = HalfEdges[i];
				}
			}
		}

		private void Flip(HalfEdge he)
		{
			System.Diagnostics.Debug.Assert(he.Twin != -1);
			var twin = HalfEdges[he.Twin];
			var nextTwin = Next(twin);
			var prevTwin = Next(nextTwin);
			var next = Next(he);
			var prev = Next(next);
			var i = Next(he.Index);
			var j = Next(twin.Index);
			HalfEdges[i] = new HalfEdge(i, prevTwin.Origin, j);
			HalfEdges[j] = new HalfEdge(j, prev.Origin, i);
			nextTwin.Index = he.Index;
			HalfEdges[he.Index] = nextTwin;
			if (nextTwin.Twin != -1) {
				MakeTwins(nextTwin.Index, nextTwin.Twin);
			}
			next.Index = twin.Index;
			HalfEdges[twin.Index] = next;
			if (next.Twin != -1) {
				MakeTwins(next.Index, next.Twin);
			}
		}

		private HalfEdge FakeTwin(HalfEdge he1, HalfEdge he2)
		{
			var twin = new HalfEdge(HalfEdges.Count, he1.Origin, HalfEdges.Count - 1);
			HalfEdges.Add(twin);
			HalfEdges.Add(new HalfEdge(-1, Next(he2).Origin, -1));
			HalfEdges.Add(HalfEdgeTopology.DummyHalfEdge);
			return twin;
		}

		private Queue<int> RemovePolygon(LinkedList<int> polygon)
		{
			RemoveTriangle(polygon.First.Value);
			RemoveTriangle(polygon.Last.Value);
			KeepConstrainedEdges(polygon.First.Value);
			KeepConstrainedEdges(polygon.Last.Value);
			polygon.Remove(polygon.First);
			polygon.Remove(polygon.Last);
			var current = polygon.First;
			var queue = new Queue<int>();
			while (current != null) {
				var next = current.Next;
				RemoveHalfEdge(Next(current.Value));
				RemoveHalfEdge(Prev(current.Value));
				KeepConstrainedEdges(current.Value);
				current = next;
			}
			current = polygon.First;
			while (current?.Next != null) {
				var he1 = HalfEdges[current.Value];
				var he2 = HalfEdges[current.Next.Value];
				he2.Index = current.Next.Value;
				var v1 = Vertices[he1.Origin].Pos;
				var v2 = Vertices[he2.Origin].Pos;
				var v3 = Vertices[HalfEdges[Next(current.Next.Value)].Origin].Pos;
				if (Area(v1, v2, v3) > 0) {
					Connect(he1, he2);
					queue.Enqueue(HalfEdges.Count - 1);
					var twin = FakeTwin(he1, he2);
					polygon.AddAfter(current.Next, twin.Index);
					RemoveTriangle(he1.Index);
					RemoveHalfEdge(he2.Index);
					polygon.Remove(current.Next);
					polygon.Remove(current);
					current = polygon.First;
					continue;
				}
				current = current.Next;
			}
			foreach (var side in polygon) {
				Untwin(side);
				RemoveTriangle(side);
			}
			return queue;
		}

		private static float PointToSegmentSqrDistance(Vector2 v, Vector2 w, Vector2 p)
		{
			var l2 = (w - v).SqrLength;
			if (l2 == 0) {
				return (p - v).SqrLength;
			}
			var t = Mathf.Max(0, Mathf.Min(1, Vector2.DotProduct(p - v, w - v) / l2));
			var proj = v + t * (w - v);
			return (p - proj).SqrLength;
		}

		private LinkedList<int> GetTriangulationBoundary(HalfEdge start)
		{
			start = RotateToBoundary(start);
			var boundary = new LinkedList<int>();
			boundary.AddFirst(start.Index);
			do {
				var prev = HalfEdges[boundary.Last.Value];
				var next = Next(prev);
				while (next.Twin != -1) {
					next = HalfEdges[Next(next.Twin)];
				}
				boundary.AddLast(next.Index);
			} while (boundary.First.Value != boundary.Last.Value);
			boundary.RemoveLast();
			return boundary;
		}

		// Origin of `start` is a pivot of rotation

		private HalfEdge RotateToBoundary(HalfEdge start)
		{
			var current = start;
			while (current.Twin != -1) {
				current = Next(Twin(current));
			}
			return current;
		}

		private LinkedList<int> GetVisibleBoundary(Vector2 vertex, LinkedList<int> boundary)
		{
			var broke = false;
			LinkedListNode<int> first = null;
			LinkedList<int> visibleBoundary = new LinkedList<int>();
			foreach (var side in boundary) {
				var currentHe = HalfEdges[side];
				var c = Vertices[currentHe.Origin].Pos;
				var d = Vertices[Next(currentHe).Origin].Pos;
				var listNode = boundary.First;
				if (Area(c, vertex, d) > 0) {
					while (listNode != null) {
						var current = HalfEdges[listNode.Value];
						var a = Vertices[current.Origin].Pos;
						var b = Vertices[Next(current).Origin].Pos;
						if (
							listNode.Value != side &&
							(b != c && PolygonMeshUtils.LineLineIntersection(a, b, vertex, c, out _) ||
							 a != d && PolygonMeshUtils.LineLineIntersection(a, b, vertex, d, out _))
						) {
							break;
						}
						listNode = listNode.Next;
					}
				}
				if (listNode != null) {
					if (!broke) {
						first = visibleBoundary.First;
					}
					broke = true;
					continue;
				}
				if (broke) {
					if (first == null) {
						visibleBoundary.AddFirst(side);
					} else {
						visibleBoundary.AddBefore(first, side);
					}
				} else {
					visibleBoundary.AddLast(side);
				}
			}
			return visibleBoundary;
		}

		public Queue<int> TriangulateVisibleBoundary(LinkedList<int> boundary, int vi)
		{
			var current = boundary.First;
			var q = new Queue<int>();
			while (current != null) {
				var v = HalfEdges[Next(current.Value)].Origin;
				Connect(v, HalfEdges[current.Value].Origin, vi);
				MakeTwins(current.Value, HalfEdges.Count - 3);
				q.Enqueue(HalfEdges.Count - 1);
				if (current.Previous != null) {
					MakeTwins(HalfEdges.Count - 2, HalfEdges.Count - 4);
				}
				current = current.Next;
			}
			return q;
		}

		private bool AreEdgesCollinear(Vector2 v11, Vector2 v12, Vector2 v21, Vector2 v22)
		{
			// p + tr = q + us
			Vector2 p = v11, q = v21, r = v12 - v11, s = v22 - v21;
			var denominator = Vector2.CrossProduct(r, s);
			var numerator = Vector2.CrossProduct((q - p), r);
			return Math.Abs(denominator) < Mathf.ZeroTolerance && Math.Abs(numerator) < Mathf.ZeroTolerance;
		}

		private HalfEdge LastValidEdge()
		{
			for (int i = HalfEdges.Count - 1; i >= 0; --i) {
				var he = HalfEdges[i];
				if (he.Index != -1) {
					return he;
				}
			}
			throw new InvalidOperationException();
		}

		public void InsertConstrainedEdge((int a, int b) ce)
		{
			var start = FindIncidentEdge(LocateTriangle(Vertices[ce.a].Pos), ce.a);
			var current = start;
			Vector2 a = Vertices[ce.a].Pos, b = Vertices[ce.b].Pos;
			var upPolygon = new LinkedList<int>();
			var downPolygon = new LinkedList<int>();
			var forward = true;
			// Rotate over point to find proper triangle to traverse further
			while (true) {
				var next = Next(current);
				var prev = Prev(current);
				// Check if constrained edge equals to existing edge of triangle (and make it constrained if it's true)
				if (next.Origin == ce.b) {
					SetEdgeConstraint(current.Index, true);
					return;
				}
				if (prev.Origin == ce.b) {
					SetEdgeConstraint(prev.Index, true);
					return;
				}
				Vector2 nextV = Vertices[next.Origin].Pos;
				Vector2 prevV = Vertices[prev.Origin].Pos;
				if (PolygonMeshUtils.LineLineIntersection(nextV, prevV, a, b, out _)) {
					break;
				}
				forward = forward ? prev.Twin != -1 : forward;
				if (current.Twin == -1) {
					return;
				}
				current = forward ? HalfEdges[prev.Twin]  : HalfEdges[Next(current.Twin)];
			}
			upPolygon.AddLast(current.Index);
			downPolygon.AddFirst(Prev(current.Index));
			// Traverse towards constrained edge direction
			RemoveTriangle(current.Index);
			current = HalfEdges[Next(current).Twin];
			while (true) {
				var next = Next(current);
				var prev = Prev(current);
				var currentV = Vertices[current.Origin].Pos;
				var nextV = Vertices[next.Origin].Pos;
				var prevV = Vertices[prev.Origin].Pos;
				if (prev.Origin == ce.b) {
					upPolygon.AddLast(next.Index);
					downPolygon.AddFirst(prev.Index);
					var first = HalfEdges[upPolygon.First.Value];
					Connect(prev.Origin, first.Origin, 0);
					Connect(first.Origin, prev.Origin, 0);
					MakeTwins(HalfEdges.Count - 3, HalfEdges.Count - 6);
					SetEdgeConstraint(HalfEdges.Count - 3, true);
					upPolygon.AddLast(HalfEdges.Count - 6);
					downPolygon.AddFirst(HalfEdges.Count - 3);
					var q1 = TriangulatePolygonByEarClipping(upPolygon);
					var q2 = TriangulatePolygonByEarClipping(downPolygon);
					RestoreDelaunayProperty(q1);
					RestoreDelaunayProperty(q2);
					return;
				}
				RemoveTriangle(current.Index);
				if (PolygonMeshUtils.LineLineIntersection(a, b, nextV, prevV, out _)) {
					downPolygon.AddFirst(prev.Index);
					current = HalfEdges[next.Twin];
				} else if (PolygonMeshUtils.LineLineIntersection(a, b, currentV, prevV, out _)) {
					upPolygon.AddLast(next.Index);
					current = HalfEdges[prev.Twin];
				}
			}
		}

		public void InsertConstrainedEdges(HashSet<(int, int)> constrainedEdges)
		{
			if (constrainedEdges.Count == 0) {
				return;
			}
			foreach (var constrainedEdge in constrainedEdges.ToList()) {
				InsertConstrainedEdge(constrainedEdge);
			}
			this.constrainedEdges.Clear();
			Topology.Invalidate();
		}

		public void DoNotKeepConstrainedEdges()
		{
			constrainedEdges.Clear();
		}

		public bool TryConcave(Vector2 position)
		{
			var triangle = LocateClosestTriangle(position, out var isInside);
			if (!isInside) {
				return false;
			}
			var used = new HashSet<int>();
			var queue = new Queue<int>();
			var verticesThatShouldProbablyBeRemoved = new HashSet<int>();
			queue.Enqueue(triangle.Index);
			queue.Enqueue(Next(triangle).Index);
			queue.Enqueue(Prev(triangle).Index);
			used.Add(triangle.Index);
			used.Add(Next(triangle).Index);
			used.Add(Prev(triangle).Index);
			while (queue.Count > 0) {
				var i = queue.Dequeue();
				var t = HalfEdges[i];
				if (TryGetTwin(i, out var twin) && !twin.Constrained) {
					var next = Next(twin.Index);
					var prev = Prev(twin.Index);
					if (used.Add(twin.Index)) {
						queue.Enqueue(twin.Index);
						verticesThatShouldProbablyBeRemoved.Add(twin.Origin);
					}
					if (used.Add(next)) {
						queue.Enqueue(next);
						verticesThatShouldProbablyBeRemoved.Add(HalfEdges[next].Origin);
					}
					if (used.Add(prev)) {
						queue.Enqueue(prev);
						verticesThatShouldProbablyBeRemoved.Add(HalfEdges[prev].Origin);
					}
				}
				if (t.Twin != -1) {
					Untwin(i);
				}
			}
			foreach (var i in used) {
				RemoveHalfEdge(i);
			}
			foreach (var vertexIndex in verticesThatShouldProbablyBeRemoved.OrderByDescending(i => i)) {
				if (IsVertexIsolated(vertexIndex)) {
					Topology.Vertices.RemoveAt(vertexIndex);
				}
			}
			return true;
		}

		private bool IsVertexIsolated(int index)
		{
			foreach (HalfEdge he in HalfEdges) {
				if (he.Index != -1 && he.Origin == index) {
					return false;
				}
			}
			return true;
		}
	}
}

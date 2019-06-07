using System;
using System.Collections.Generic;
using System.Linq;
using Lime.PolygonMesh;
using HalfEdge = Lime.PolygonMesh.Geometry.HalfEdge;

namespace Lime.Source.Optimizations
{
	public class Triangulator
	{
		public static Triangulator Instance { get; } = new Triangulator();

		public void AddVertex(Geometry geometry, Vertex vertex)
		{
			geometry.Vertices.Add(vertex);
			Triangulate(geometry, GetContourPolygon(geometry, LocateTriangle(geometry, geometry.HalfEdges[0], vertex), vertex), geometry.Vertices.Count - 1);
		}

		public void RemoveVertex(Geometry geometry, int vi)
		{
			var vertex = geometry.Vertices[vi];
			var polygon = GetBoundaryPolygon(geometry, FindIncidentEdge(geometry, LocateTriangle(geometry, geometry.HalfEdges[0], vertex), vi));
			RestoreDelaunayProperty(geometry,
				geometry.HalfEdges[geometry.Next(polygon.Last.Value)].Origin == vi ?
					RemovePolygon(geometry, polygon) :
					TriangulateByEarClipping(geometry, polygon));
			geometry.Vertices[vi] = geometry.Vertices[geometry.Vertices.Count - 1];
			geometry.Vertices.RemoveAt(geometry.Vertices.Count - 1);
			geometry.Invalidate(vi);
			System.Diagnostics.Debug.Assert(FullCheck(geometry));
		}

		private bool FullCheck(Geometry geometry)
		{
			for (int i = 0; i < geometry.HalfEdges.Count; i += 3) {
				if (
					geometry.HalfEdges[i].Index == -1 || geometry.HalfEdges[i + 1].Index == -1 ||
					geometry.HalfEdges[i + 2].Index == -1
				) {
					continue;
				}
				int p = geometry.HalfEdges[i].Origin,
					q = geometry.HalfEdges[i + 1].Origin,
					r = geometry.HalfEdges[i + 2].Origin;
				var he = geometry.HalfEdges[i];
				for (int j = 0; j < geometry.Vertices.Count; j++) {
					if (j != p && j != q && j != r) {
						if (InCircumcircle(geometry, he, geometry.Vertices[j])) {
							return false;
						}
					}
				}
			}
			return true;
		}

		private HalfEdge LocateTriangle(Geometry geometry, HalfEdge start, Vertex vertex)
		{
			var current = geometry.HalfEdges[0];
			do {
				var next = geometry.Next(current);
				Vector2 p1 = geometry.Vertices[current.Origin].Pos;
				var side = geometry.Vertices[next.Origin].Pos - p1;
				var v1 = geometry.Vertices[geometry.Next(next).Origin].Pos - p1;
				var v2 = vertex.Pos - p1;
				if (
					Mathf.Sign(Vector2.CrossProduct(side, v1)) * Mathf.Sign(Vector2.CrossProduct(side, v2)) < 0 &&
					current.Twin != -1
				) {
					start = current = geometry.HalfEdges[current.Twin];
				}
				current = geometry.Next(current);
			} while (current.Index != start.Index);
			return current;
		}

		private HalfEdge FindIncidentEdge(Geometry geometry, HalfEdge edge, int vi)
		{
			return edge.Origin == vi ? edge :
				(edge = geometry.Next(edge)).Origin == vi ? edge :
					 (edge = geometry.Next(edge)).Origin == vi ? edge : throw new InvalidOperationException();
		}

		private bool InCircumcircle(Geometry geometry, HalfEdge edge, Vertex vertex)
		{
			var v1 = geometry.Vertices[edge.Origin].Pos;
			var next = geometry.Next(edge);
			var v2 = geometry.Vertices[next.Origin].Pos;
			var v3 = geometry.Vertices[geometry.Next(next).Origin].Pos;
			var p = vertex.Pos;
			var n1 = (double)v1.SqrLength;
			var n2 = (double)v2.SqrLength;
			var n3 = (double)v3.SqrLength;
			var a = (double)v1.X * v2.Y + (double)v2.X * v3.Y + (double)v3.X * v1.Y -
					((double)v2.Y * v3.X + (double)v3.Y * v1.X + (double)v1.Y * v2.X);
			var b = n1 * v2.Y + n2 * v3.Y + n3 * v1.Y - (v2.Y * n3 + v3.Y * n1 + v1.Y * n2);
			var c = n1 * v2.X + n2 * v3.X + n3 * v1.X - (v2.X * n3 + v3.X * n1 + v1.X * n2);
			var d = n1 * v2.X * v3.Y + n2 * v3.X * v1.Y + n3 * v1.X * v2.Y - (v2.X * n3 * v1.Y + v3.X * n1 * v2.Y + v1.X * n2 * v3.Y);
			return (a * p.SqrLength - b * p.X + c * p.Y - d) * Math.Sign(a) < 0;
		}

		private LinkedList<int> GetContourPolygon(Geometry geometry, HalfEdge start, Vertex vertex)
		{
			var polygon = new LinkedList<int>();
			polygon.AddLast(start.Index);
			polygon.AddLast(geometry.Next(start.Index));
			polygon.AddLast(geometry.Prev(start.Index));
			var current = polygon.First;
			while (current != null) {
				var edge = geometry.HalfEdges[current.Value];
				if (edge.Twin != -1) {
					var twin = geometry.HalfEdges[edge.Twin];
					if (InCircumcircle(geometry, geometry.HalfEdges[edge.Twin], vertex)) {
						polygon.AddAfter(current, geometry.Next(twin.Index));
						polygon.AddAfter(current.Next, geometry.Prev(twin.Index));
						geometry.HalfEdges[edge.Index] = new HalfEdge(-1, edge.Origin, edge.Twin);
						geometry.HalfEdges[twin.Index] = new HalfEdge(-1, twin.Origin, twin.Twin);
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

		private LinkedList<int> GetBoundaryPolygon(Geometry geometry, HalfEdge incident)
		{
			var polygon = new LinkedList<int>();
			bool reverse = false;
			var current = incident;
			while (current.Index != -1) {
				var i = current.Index;
				if (reverse) {
					polygon.AddFirst(geometry.Next(i));
				} else {
					polygon.AddLast(geometry.Next(i));
				}
				var next = geometry.HalfEdges[reverse ? i : geometry.Next(polygon.Last.Value)];
				if (next.Twin == -1) {
					if (reverse) {
						polygon.AddFirst(next.Index);
						break;
					}
					polygon.RemoveFirst();
					polygon.AddLast(next.Index);
					reverse = true;
					current = incident;
					continue;
				}
				geometry.RemoveHalfEdge(current.Index);
				current = reverse ? geometry.HalfEdges[geometry.Next(next.Twin)] : geometry.HalfEdges[next.Twin];
				geometry.RemoveHalfEdge(next.Index);
			}
			return polygon;
		}

		private void Connect(Geometry geometry, HalfEdge he, int vi)
		{
			geometry.Connect(he.Origin, geometry.Next(he).Origin, vi);
			if (he.Twin != -1) {
				geometry.MakeTwins(he.Twin, geometry.HalfEdges.Count - 3);
			}
		}

		private void Connect(Geometry geometry, HalfEdge he1, HalfEdge he2)
		{
			geometry.Connect(he1.Origin, he2.Origin, geometry.Next(he2).Origin);
			if (he1.Twin != -1) {
				geometry.MakeTwins(he1.Twin, geometry.HalfEdges.Count - 3);
			}
			if (he2.Twin != -1) {
				geometry.MakeTwins(he2.Twin, geometry.HalfEdges.Count - 2);
			}
		}

		private float Area(Vector2 v1, Vector2 v2, Vector2 v3) => (v2.X - v1.X) * (v3.Y - v1.Y) - (v2.Y - v1.Y) * (v3.X - v1.X);

		private void Triangulate(Geometry geometry, LinkedList<int> polygon, int vi)
		{
			var current = polygon.First;
			while (current != null) {
				var edge = geometry.HalfEdges[current.Value];
				Connect(geometry, geometry.HalfEdges[current.Value], vi);
				if (current.Previous != null) {
					geometry.MakeTwins(geometry.HalfEdges.Count - 1, geometry.Next(current.Previous.Value));
				}
				edge.Index = -1;
				geometry.HalfEdges[current.Value] = edge;
				current.Value = geometry.HalfEdges.Count - 3;
				current = current.Next;
			}
			geometry.MakeTwins(geometry.Next(polygon.Last.Value), geometry.Prev(polygon.First.Value));
			geometry.Invalidate();
		}

		private Queue<int> TriangulateByEarClipping(Geometry geometry, LinkedList<int> polygon)
		{
			var queue = new Queue<int>();
			bool CanCreateTriangle(int cur, int next)
			{
				HalfEdge he1 = geometry.HalfEdges[cur];
				int o1 = he1.Origin,
					o2 = geometry.HalfEdges[next].Origin,
					o3 = geometry.HalfEdges[geometry.Next(next)].Origin;
				Vector2 v1 = geometry.Vertices[o1].Pos,
						v2 = geometry.Vertices[o2].Pos,
						v3 = geometry.Vertices[o3].Pos;
				if (Vector2.DotProduct(new Vector2(-(v2 - v1).Y, (v2 - v1).X), v3 - v2) <= 0) {
					return false;
				}
				foreach (var i in polygon) {
					var j = geometry.HalfEdges[i].Origin;
					if (j != o1 && j != o2 && j != o3) {
						var v = geometry.Vertices[j].Pos;
						if (Area(v, v1, v2) >= 0 && Area(v, v2, v3) >= 0 && Area(v, v3, v1) >= 0) {
							return false;
						}
					}
				}
				return true;
			}
			var current = polygon.First;
			while (polygon.Count > 2) {
				var next = current.Next ?? polygon.First;
				if (CanCreateTriangle(current.Value, next.Value)) {
					var he1 = geometry.HalfEdges[current.Value];
					var he2 = geometry.HalfEdges[next.Value];
					Connect(geometry, he1, he2);
					queue.Enqueue(geometry.HalfEdges.Count - 1);
					var twin = new HalfEdge(geometry.HalfEdges.Count, he1.Origin, geometry.HalfEdges.Count - 1);
					polygon.AddAfter(next, geometry.HalfEdges.Count);
					geometry.HalfEdges.Add(twin);
					geometry.HalfEdges.Add(new HalfEdge(-1, geometry.Next(he2).Origin, -1));
					geometry.HalfEdges.Add(Geometry.DummyHalfEdge);
					he1.Index = -1;
					he2.Index = -1;
					geometry.HalfEdges[next.Value] = he2;
					geometry.HalfEdges[current.Value] = he1;
					polygon.Remove(next);
					polygon.Remove(current);
					current = polygon.First;
				} else {
					current = next;
				}
			}
			System.Diagnostics.Debug.Assert(polygon.Count == 2);
			var last = geometry.HalfEdges[polygon.Last.Value];
			var lastOriginal = geometry.HalfEdges[geometry.HalfEdges[polygon.First.Value].Twin];
			lastOriginal.Twin = last.Twin;
			if (last.Twin != -1) {
				geometry.MakeTwins(last.Twin, lastOriginal.Index);
			}
			last.Index = -1;
			geometry.HalfEdges[polygon.Last.Value] = last;
			geometry.HalfEdges[lastOriginal.Index] = lastOriginal;
			queue.Enqueue(lastOriginal.Index);
			geometry.HalfEdges[polygon.First.Value] = Geometry.DummyHalfEdge;
			return queue;
		}

		private void RestoreDelaunayProperty(Geometry geometry, Queue<int> queue)
		{
			while (queue.Count > 0) {
				var i = queue.Dequeue();
				var he = geometry.HalfEdges[i];
				for (int j = 0; j < 3; ++j) {
					if (
						he.Twin != -1 &&
						InCircumcircle(geometry, he, geometry.Vertices[geometry.Prev(geometry.HalfEdges[he.Twin]).Origin])
					) {
						Flip(geometry, he);
						queue.Enqueue(i);
						queue.Enqueue(geometry.HalfEdges[geometry.Next(i)].Twin);
						break;
					}
					i = geometry.Next(i);
					he = geometry.HalfEdges[i];
				}
			}
		}

		private void Flip(Geometry geometry, HalfEdge he)
		{
			System.Diagnostics.Debug.Assert(he.Twin != -1);
			var twin = geometry.HalfEdges[he.Twin];
			var nextTwin = geometry.Next(twin);
			var prevTwin = geometry.Next(nextTwin);
			var next = geometry.Next(he);
			var prev = geometry.Next(next);
			var i = geometry.Next(he.Index);
			var j = geometry.Next(twin.Index);
			geometry.HalfEdges[i] = new HalfEdge(i, prevTwin.Origin, j);
			geometry.HalfEdges[j] = new HalfEdge(j, prev.Origin, i);
			nextTwin.Index = he.Index;
			geometry.HalfEdges[he.Index] = nextTwin;
			if (nextTwin.Twin != -1) {
				geometry.MakeTwins(nextTwin.Index, nextTwin.Twin);
			}
			next.Index = twin.Index;
			geometry.HalfEdges[twin.Index] = next;
			if (next.Twin != -1) {
				geometry.MakeTwins(next.Index, next.Twin);
			}
		}

		private HalfEdge FakeTwin(Geometry geometry, HalfEdge he1, HalfEdge he2)
		{
			var twin = new HalfEdge(geometry.HalfEdges.Count, he1.Origin, geometry.HalfEdges.Count - 1);
			geometry.HalfEdges.Add(twin);
			geometry.HalfEdges.Add(new HalfEdge(-1, geometry.Next(he2).Origin, -1));
			geometry.HalfEdges.Add(Geometry.DummyHalfEdge);
			return twin;
		}

		private Queue<int> RemovePolygon(Geometry geometry, LinkedList<int> polygon)
		{
			var current = polygon.First;
			var queue = new Queue<int>();
			while (current != null) {
				var next = current.Next;
				if (geometry.HalfEdges[current.Value].Twin == -1) {
					polygon.Remove(current);
					geometry.RemoveHalfEdge(current.Value);
				} else {
					geometry.RemoveHalfEdge(geometry.Next(current.Value));
					geometry.RemoveHalfEdge(geometry.Prev(current.Value));
				}
				current = next;
			}
			current = polygon.First;
			while (current?.Next != null) {
				var he1 = geometry.HalfEdges[current.Value];
				var he2 = geometry.HalfEdges[current.Next.Value];
				var v1 = geometry.Vertices[he1.Origin].Pos;
				var v2 = geometry.Vertices[he2.Origin].Pos;
				var v3 = geometry.Vertices[geometry.HalfEdges[geometry.Next(current.Next.Value)].Origin].Pos;
				if (Area(v1, v2, v3) > 0) {
					Connect(geometry, he1, he2);
					queue.Enqueue(geometry.HalfEdges.Count - 1);
					var twin = FakeTwin(geometry, he1, he2);
					polygon.AddAfter(current.Next, twin.Index);
					geometry.RemoveHalfEdge(he1.Index);
					geometry.RemoveHalfEdge(he2.Index);
					polygon.Remove(current.Next);
					polygon.Remove(current);
					current = polygon.First;
					continue;
				}
				current = current.Next;
			}
			foreach (var side in polygon) {
				geometry.Untwin(side);
				geometry.RemoveHalfEdge(side);
			}
			return queue;
		}
	}
}

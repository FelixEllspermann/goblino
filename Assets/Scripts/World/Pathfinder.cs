using System;
using System.Collections.Generic;
using Unity.Mathematics;

namespace RTSCL.World
{
    /// <summary>Grid A* (8-directional, octile heuristic, no diagonal corner-cutting).
    /// Passability is injected so this is decoupled from Unity tilemaps and unit-testable.</summary>
    public static class Pathfinder
    {
        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };
        private const float SQRT2 = 1.41421356f;

        private static int _w, _h, _curGen;
        private static float[] _g;
        private static int[] _from;
        private static int[] _gen;
        private static float[] _heapF;
        private static int[] _heapI;
        private static int _heapCount;

        /// <summary>Cells from start (exclusive) to goal (inclusive), or null if unreachable / cap exceeded.</summary>
        public static List<int2> FindPath(int2 start, int2 goal, int width, int height,
                                          Func<int, int, bool> passable, int maxExpansions = 6000)
        {
            if (passable == null || width <= 0 || height <= 0) return null;
            if (!InBounds(start.x, start.y, width, height) || !InBounds(goal.x, goal.y, width, height)) return null;
            if (!passable(goal.x, goal.y)) return null;
            if (start.x == goal.x && start.y == goal.y) return new List<int2>();

            EnsureBuffers(width, height);
            _curGen++;
            _heapCount = 0;

            int si = start.y * _w + start.x;
            int gi = goal.y * _w + goal.x;
            _g[si] = 0f; _from[si] = -1; _gen[si] = _curGen;
            HeapPush(Heuristic(start.x, start.y, goal.x, goal.y), si);

            int expansions = 0;
            while (_heapCount > 0)
            {
                int cur = HeapPop();
                if (cur == gi) return Reconstruct(cur, si);
                if (++expansions > maxExpansions) return null;

                int cx = cur % _w, cy = cur / _w;
                float cg = _g[cur];
                for (int d = 0; d < 8; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (!InBounds(nx, ny, _w, _h)) continue;
                    if (!passable(nx, ny)) continue;
                    bool diagonal = d >= 4;
                    if (diagonal && (!passable(cx, ny) || !passable(nx, cy))) continue; // no corner cut
                    int ni = ny * _w + nx;
                    float ng = cg + (diagonal ? SQRT2 : 1f);
                    if (_gen[ni] != _curGen || ng < _g[ni])
                    {
                        _g[ni] = ng;
                        _from[ni] = cur;
                        _gen[ni] = _curGen;
                        HeapPush(ng + Heuristic(nx, ny, goal.x, goal.y), ni);
                    }
                }
            }
            return null;
        }

        private static bool InBounds(int x, int y, int w, int h) => x >= 0 && x < w && y >= 0 && y < h;

        private static float Heuristic(int ax, int ay, int bx, int by)
        {
            int dx = Math.Abs(ax - bx), dy = Math.Abs(ay - by);
            int mn = Math.Min(dx, dy), mx = Math.Max(dx, dy);
            return (mx - mn) + SQRT2 * mn;
        }

        private static List<int2> Reconstruct(int goalIdx, int startIdx)
        {
            var rev = new List<int2>();
            int cur = goalIdx;
            while (cur != startIdx && cur != -1)
            {
                rev.Add(new int2(cur % _w, cur / _w));
                cur = _from[cur];
            }
            rev.Reverse();
            return rev;
        }

        private static void EnsureBuffers(int w, int h)
        {
            if (_g != null && _w == w && _h == h) return;
            _w = w; _h = h;
            int n = w * h;
            _g = new float[n];
            _from = new int[n];
            _gen = new int[n];
            _heapF = new float[1024];
            _heapI = new int[1024];
            _curGen = 0;
        }

        private static void HeapPush(float f, int idx)
        {
            if (_heapCount + 1 >= _heapF.Length)
            {
                Array.Resize(ref _heapF, _heapF.Length * 2);
                Array.Resize(ref _heapI, _heapI.Length * 2);
            }
            int i = ++_heapCount;
            _heapF[i] = f; _heapI[i] = idx;
            while (i > 1)
            {
                int p = i >> 1;
                if (_heapF[p] <= _heapF[i]) break;
                Swap(p, i); i = p;
            }
        }

        private static int HeapPop()
        {
            int top = _heapI[1];
            _heapF[1] = _heapF[_heapCount];
            _heapI[1] = _heapI[_heapCount];
            _heapCount--;
            int i = 1;
            while (true)
            {
                int l = i << 1, r = l + 1, m = i;
                if (l <= _heapCount && _heapF[l] < _heapF[m]) m = l;
                if (r <= _heapCount && _heapF[r] < _heapF[m]) m = r;
                if (m == i) break;
                Swap(m, i); i = m;
            }
            return top;
        }

        private static void Swap(int a, int b)
        {
            (_heapF[a], _heapF[b]) = (_heapF[b], _heapF[a]);
            (_heapI[a], _heapI[b]) = (_heapI[b], _heapI[a]);
        }
    }
}

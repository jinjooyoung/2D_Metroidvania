#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// 에디터에서 타일을 찍을 때마다 LevelTilemap을 자동 동기화.
/// [자동] 레이어에 실수로 찍거나 지운 것도 Level 타일맵으로 옮겨서 반영.
/// </summary>
[InitializeOnLoad]
static class LevelTilemapAutoSync
{
    struct Mirror { public LevelTilemap level; public Tilemap layer; public Vector3Int pos; public TileBase tile; }

    static readonly HashSet<LevelTilemap> dirty = new HashSet<LevelTilemap>();
    static readonly List<Mirror> mirrors = new List<Mirror>();
    static bool scheduled, syncing;

    static LevelTilemapAutoSync()
    {
        Tilemap.tilemapTileChanged -= OnChanged;
        Tilemap.tilemapTileChanged += OnChanged;
    }

    static void OnChanged(Tilemap tm, Tilemap.SyncTile[] changes)
    {
        if (syncing || tm == null || EditorApplication.isPlayingOrWillChangePlaymode) return;

        LevelTilemap level = tm.GetComponent<LevelTilemap>();
        if (level == null)
        {
            Grid grid = tm.GetComponentInParent<Grid>();
            if (grid == null) return;
            foreach (LevelTilemap l in grid.GetComponentsInChildren<LevelTilemap>())
                if (l.IsGeneratedLayer(tm)) { level = l; break; }
            if (level == null) return;

            foreach (Tilemap.SyncTile c in changes)
                mirrors.Add(new Mirror { level = level, layer = tm, pos = c.position, tile = c.tile });
        }

        dirty.Add(level);
        if (!scheduled)
        {
            scheduled = true;
            EditorApplication.delayCall += Flush;
        }
    }

    static void Flush()
    {
        scheduled = false;
        syncing = true;
        try
        {
            foreach (Mirror m in mirrors)
            {
                if (m.level == null) continue;
                Tilemap src = m.level.Source;
                TileBase current = src.GetTile(m.pos);
                if (m.tile != null)
                {
                    if (current != m.tile) src.SetTile(m.pos, m.tile);            // [자동] 레이어에 찍음 → Level로
                }
                else if (current != null && LevelTilemap.IsKind(current, m.level.KindsOf(m.layer)))
                {
                    src.SetTile(m.pos, null);                                     // [자동] 레이어에서 지움 → Level에서도
                }
            }
            mirrors.Clear();

            foreach (LevelTilemap l in dirty)
            {
                if (l == null) continue;
                l.Sync();
                EditorSceneManager.MarkSceneDirty(l.gameObject.scene);
            }
            dirty.Clear();
        }
        finally { syncing = false; }
    }

    [MenuItem("Tools/Metroidvania/레벨 타일 다시 동기화")]
    static void SyncAll()
    {
#if UNITY_2023_1_OR_NEWER
        LevelTilemap[] all = Object.FindObjectsByType<LevelTilemap>(FindObjectsSortMode.None);
#else
        LevelTilemap[] all = Object.FindObjectsOfType<LevelTilemap>();
#endif
        foreach (LevelTilemap l in all)
        {
            l.Sync();
            EditorSceneManager.MarkSceneDirty(l.gameObject.scene);
        }
        Debug.Log($"[LevelTilemap] {all.Length}개 동기화 완료");
    }
}
#endif

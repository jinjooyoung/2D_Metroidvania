#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// 「선」 세부 맵(엑셀 '맵 배치표'·챕터 시트와 같은 데이터)을 현재 씬의 Level 타일맵에 그리는 에디터.
/// 메뉴: Tools > Metroidvania > 세부 맵 에디터 (선 월드맵)
///
/// 데이터: Assets/_Project/LevelData/SunWorldMap.txt (1글자 = 1타일, 월드 좌표 그대로)
///   #  벽·바닥·천장 (T_Ground)          X  붙을 수 없는 벽 (T_WallNoClimb)
///   =  통과 발판 (T_Platform)           ^  가시 (T_Spike)
///   U  상승기류 (T_Updraft)             L  끌림 (T_Pull)
///   S  저장 지점 (T_SavePoint)          K  즉사 구역 (KillZone 오브젝트)
///   M  몬스터 (Enemy 프리팹, 세로 2칸)  a/b/c  능력 조각 벽타기/더블점프/대쉬 (AbilityPickup)
///   H  따뜻한 기억 (HealPickup)         N/T/~  NPC·흔적·빛의 길 (기획 마커, 게임 동작 없음)
/// </summary>
public class WorldMapEditor : EditorWindow
{
    const string DefaultDataPath = "Assets/_Project/LevelData/SunWorldMap.txt";
    const string TileDir = "Assets/_Project/Tiles";
    const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemy.prefab";
    const string SquarePath = "Assets/_Project/Art/Square.png";
    public const string RootPrefix = "[세부 맵] ";
    public const string LabelPrefKey = "SunMap.ShowLabels";

    TextAsset data;
    MapData map;
    string mapError;
    int scope;                       // 0 = 전체, 1.. = 챕터
    Vector2Int offset = Vector2Int.zero;
    bool clearArea = true;
    bool placeEnemies = true;
    bool removeOtherEnemies = true;
    bool makeKillZones = true;
    bool makeItems = true;
    bool makeMarkers = true;
    bool makeLabels = true;
    bool movePlayer = true;
    bool applyAbilities = true;
    bool frameSceneView = true;
    Vector2 scroll;

    [MenuItem("Tools/Metroidvania/세부 맵 에디터 (선 월드맵)")]
    static void Open() => GetWindow<WorldMapEditor>("세부 맵");

    void OnEnable()
    {
        if (data == null) data = AssetDatabase.LoadAssetAtPath<TextAsset>(DefaultDataPath);
        Reload();
    }

    void Reload()
    {
        map = null; mapError = null;
        if (data == null) { mapError = "데이터 파일이 없습니다. " + DefaultDataPath; return; }
        try { map = MapData.Parse(data.text); }
        catch (Exception e) { mapError = "데이터를 읽지 못했습니다: " + e.Message; }
    }

    // ───────────────────────── GUI ─────────────────────────
    void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.LabelField("「선」 세부 맵 그리기", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "엑셀 세부 맵과 같은 데이터를 Level 타일맵에 그립니다. 1칸 = 1타일(1m), 월드 좌표 그대로.\n" +
            "실행 전 씬 저장을 권장합니다 (Ctrl+Z로 되돌리기 가능하지만 전체 맵은 무겁습니다).",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        data = (TextAsset)EditorGUILayout.ObjectField("맵 데이터", data, typeof(TextAsset), false);
        if (EditorGUI.EndChangeCheck()) Reload();
        if (GUILayout.Button("데이터 다시 읽기")) Reload();

        if (map == null)
        {
            EditorGUILayout.HelpBox(mapError ?? "데이터 없음", MessageType.Error);
            EditorGUILayout.EndScrollView();
            return;
        }
        EditorGUILayout.LabelField($"맵 크기 {map.w} × {map.h}, 방 {map.rooms.Count}개, 챕터 {map.chapters.Count}개", EditorStyles.miniLabel);

        EditorGUILayout.Space();
        var options = new List<string> { "전체 맵" };
        foreach (Chapter c in map.chapters) options.Add(c.name);
        scope = EditorGUILayout.Popup("그릴 범위", scope, options.ToArray());
        offset = EditorGUILayout.Vector2IntField("위치 오프셋 (타일)", offset);

        EditorGUILayout.Space();
        clearArea = EditorGUILayout.Toggle(new GUIContent("범위 안 기존 타일 지우기", "전체 맵이면 Level 타일맵 전체를 비웁니다"), clearArea);
        placeEnemies = EditorGUILayout.Toggle("몬스터 배치 (Enemy 프리팹)", placeEnemies);
        removeOtherEnemies = EditorGUILayout.Toggle(new GUIContent("씬의 다른 몬스터 제거", "레벨 초안 에디터 등으로 놓은 몬스터"), removeOtherEnemies);
        makeKillZones = EditorGUILayout.Toggle("즉사 구역 생성 (KillZone)", makeKillZones);
        makeItems = EditorGUILayout.Toggle("능력 조각·회복 아이템 생성", makeItems);
        makeMarkers = EditorGUILayout.Toggle("기획 마커 (NPC·흔적·빛의 길)", makeMarkers);
        makeLabels = EditorGUILayout.Toggle("방 이름 라벨", makeLabels);
        movePlayer = EditorGUILayout.Toggle("플레이어를 챕터 시작 지점에 배치", movePlayer);
        applyAbilities = EditorGUILayout.Toggle("챕터 시작 능력 상태 적용", applyAbilities);
        frameSceneView = EditorGUILayout.Toggle("씬 뷰를 범위에 맞춤", frameSceneView);

        bool labels = EditorPrefs.GetBool(LabelPrefKey, true);
        bool newLabels = EditorGUILayout.Toggle("씬 뷰 라벨 표시", labels);
        if (newLabels != labels) { EditorPrefs.SetBool(LabelPrefKey, newLabels); SceneView.RepaintAll(); }

        Chapter start = StartChapter();
        if (start != null)
            EditorGUILayout.HelpBox(
                $"시작: {start.name} ({start.start.x}, {start.start.y})  |  능력: 벽타기 {(start.wall ? "ON" : "OFF")}, " +
                $"더블점프 {(start.dbl ? "ON" : "OFF")}, 대쉬 {(start.dash ? "ON" : "OFF")}  |  몬스터 예비동작 {start.windup:0.0}초",
                MessageType.None);

        EditorGUILayout.Space();
        if (GUILayout.Button("그리기", GUILayout.Height(34))) Draw();
        if (GUILayout.Button("세부 맵 오브젝트만 지우기")) { RemoveRoots(null); }
        EditorGUILayout.EndScrollView();
    }

    Chapter StartChapter()
    {
        if (map == null || map.chapters.Count == 0) return null;
        return scope == 0 ? map.chapters[0] : map.chapters[scope - 1];
    }

    // ───────────────────────── 그리기 ─────────────────────────
    void Draw()
    {
        LevelTilemap lt = FindFirst<LevelTilemap>();
        if (lt == null)
        {
            EditorUtility.DisplayDialog("세부 맵",
                "씬에 LevelTilemap(Level 타일맵)이 없습니다.\n" +
                "Tools > Metroidvania > 현재 씬을 단일 타일맵(Level) 방식으로 변환 을 먼저 실행하세요.", "확인");
            return;
        }

        TileBase ground = Load("T_Ground"), platform = Load("T_Platform"), spike = Load("T_Spike"),
                 updraft = Load("T_Updraft"), save = Load("T_SavePoint"), pull = Load("T_Pull"), noClimb = Load("T_WallNoClimb");
        if (ground == null || platform == null || spike == null || updraft == null || save == null)
        {
            EditorUtility.DisplayDialog("세부 맵", TileDir + " 에서 기본 타일 5종(T_Ground·T_Platform·T_Spike·T_Updraft·T_SavePoint)을 찾지 못했습니다.", "확인");
            return;
        }
        if (pull == null || noClimb == null || lt.pullLayer == null || lt.noClimbLayer == null)
            Debug.LogWarning("[세부 맵] 끌림/붙을 수 없는 벽 타일 또는 레이어가 없습니다. Tools > Metroidvania > 기능 업데이트 적용 을 먼저 실행하세요. " +
                             "(끌림은 생략, 붙을 수 없는 벽은 일반 벽으로 그림)");
        if (noClimb == null) noClimb = ground;

        RectInt area = scope == 0 ? new RectInt(map.x0, map.y0, map.w, map.h) : map.chapters[scope - 1].box;
        string title = scope == 0 ? "전체 맵" : map.chapters[scope - 1].name;
        if (!EditorUtility.DisplayDialog("세부 맵", $"{title} ({area.width} × {area.height})을 그립니다.\n" +
                (clearArea ? "범위 안의 기존 Level 타일을 지웁니다. " : "") + "계속할까요?", "그리기", "취소"))
            return;

        try
        {
            EditorUtility.DisplayProgressBar("세부 맵", "타일 배치 중...", 0.2f);
            RecordTilemaps(lt, "세부 맵 그리기");
            Tilemap tm = lt.Source;

            if (clearArea)
            {
                if (scope == 0) tm.ClearAllTiles();
                else
                {
                    var b = new BoundsInt(area.xMin + offset.x, area.yMin + offset.y, 0, area.width, area.height, 1);
                    tm.SetTilesBlock(b, new TileBase[area.width * area.height]);
                }
            }

            var pos = new List<Vector3Int>(area.width * area.height / 3);
            var tiles = new List<TileBase>(pos.Capacity);
            for (int x = area.xMin; x < area.xMax; x++)
            for (int y = area.yMin; y < area.yMax; y++)
            {
                TileBase t = null;
                switch (map.Get(x, y))
                {
                    case '#': t = ground; break;
                    case 'X': t = noClimb; break;
                    case '=': t = platform; break;
                    case '^': t = spike; break;
                    case 'U': t = updraft; break;
                    case 'S': t = save; break;
                    case 'L': t = pull; break;
                }
                if (t == null) continue;
                pos.Add(new Vector3Int(x + offset.x, y + offset.y, 0));
                tiles.Add(t);
            }
            tm.SetTiles(pos.ToArray(), tiles.ToArray());

            EditorUtility.DisplayProgressBar("세부 맵", "충돌 레이어 동기화 중...", 0.5f);
            tm.CompressBounds();
            lt.Sync();
            RefreshColliders(lt);
            PullTilemap pullMap = lt.pullLayer != null ? lt.pullLayer.GetComponent<PullTilemap>() : null;
            if (pullMap != null) pullMap.Rebuild();

            EditorUtility.DisplayProgressBar("세부 맵", "오브젝트 배치 중...", 0.8f);
            if (removeOtherEnemies) RemoveForeignEnemies();
            RemoveRoots(scope == 0 ? null : title);
            SpawnObjects(area, title);

            Chapter start = StartChapter();
            if (movePlayer && start != null) PlacePlayerAndCamera(start);
            if (applyAbilities && start != null) ApplyAbilities(start);

            if (frameSceneView && SceneView.lastActiveSceneView != null)
            {
                Vector3 c = new Vector3(area.center.x + offset.x, area.center.y + offset.y, 0f);
                SceneView.lastActiveSceneView.Frame(new Bounds(c, new Vector3(area.width, area.height, 1f)), false);
            }
            EditorSceneManager.MarkSceneDirty(lt.gameObject.scene);
            Debug.Log($"[세부 맵] {title} 그리기 완료: 타일 {pos.Count}개");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ───────────────────────── 오브젝트 ─────────────────────────
    void SpawnObjects(RectInt area, string title)
    {
        GameObject root = new GameObject(RootPrefix + title);
        Undo.RegisterCreatedObjectUndo(root, "세부 맵 오브젝트");
        Transform enemies = Child(root.transform, "몬스터");
        Transform kills = Child(root.transform, "즉사 구역");
        Transform items = Child(root.transform, "아이템");
        Transform markers = Child(root.transform, "기획 마커");
        Transform labels = Child(root.transform, "라벨");
        markers.gameObject.tag = "EditorOnly";
        labels.gameObject.tag = "EditorOnly";

        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
        GameObject enemyPrefab = placeEnemies ? AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath) : null;
        if (placeEnemies && enemyPrefab == null) Debug.LogWarning("[세부 맵] Enemy 프리팹을 찾지 못해 몬스터를 배치하지 않았습니다.");

        int nEnemy = 0, nKill = 0, nItem = 0, nMarker = 0;
        for (int y = area.yMin; y < area.yMax; y++)
        {
            for (int x = area.xMin; x < area.xMax; x++)
            {
                char c = map.Get(x, y);
                Vector3 cell = new Vector3(x + offset.x, y + offset.y, 0f);

                if (c == 'M' && enemyPrefab != null && map.Get(x, y - 1) != 'M')
                {
                    GameObject e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, enemies);
                    e.transform.position = cell + new Vector3(0.5f, 0f, 0f);
                    EnemyAI ai = e.GetComponent<EnemyAI>();
                    if (ai != null)
                    {
                        ai.attackWindup = map.WindupAt(x, y);
                        PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
                    }
                    nEnemy++;
                }
                else if (c == 'K' && makeKillZones && map.Get(x - 1, y) != 'K')
                {
                    int len = Run(x, y, 'K', area);
                    GameObject k = Box(kills, "즉사 " + x + "," + y, square, Color.black,
                        cell + new Vector3(len * 0.5f, 0.5f, 0f), new Vector2(len, 1f), 5);
                    BoxCollider2D col = k.AddComponent<BoxCollider2D>();
                    col.isTrigger = true;
                    k.AddComponent<KillZone>();
                    nKill++;
                }
                else if ((c == 'a' || c == 'b' || c == 'c') && makeItems)
                {
                    var ab = c == 'a' ? AbilityPickup.Ability.WallJump : c == 'b' ? AbilityPickup.Ability.DoubleJump : AbilityPickup.Ability.Dash;
                    string n = c == 'a' ? "벽타기" : c == 'b' ? "더블점프" : "대쉬";
                    GameObject g = Box(items, "능력 조각: " + n, square, Hex("4A86E8"), cell + new Vector3(0.5f, 0.5f, 0f), new Vector2(0.6f, 0.6f), 30);
                    g.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    CircleCollider2D cc = g.AddComponent<CircleCollider2D>();
                    cc.isTrigger = true; cc.radius = 0.9f;
                    g.AddComponent<AbilityPickup>().ability = ab;
                    nItem++;
                }
                else if (c == 'H' && makeItems)
                {
                    GameObject g = Box(items, "따뜻한 기억", square, Hex("9BE88A"), cell + new Vector3(0.5f, 0.5f, 0f), new Vector2(0.5f, 0.5f), 30);
                    CircleCollider2D cc = g.AddComponent<CircleCollider2D>();
                    cc.isTrigger = true; cc.radius = 0.9f;
                    g.AddComponent<HealPickup>();
                    nItem++;
                }
                else if (makeMarkers && (c == 'N' || c == 'T'))
                {
                    if (c == 'N' && map.Get(x, y - 1) == 'N') continue;      // 세로 2칸 NPC는 한 번만
                    bool npc = c == 'N';
                    Box(markers, npc ? "[NPC] 선 NPC" : "[흔적] 흔적 조각", square, npc ? Hex("7EC8FF") : Hex("FFE066"),
                        cell + new Vector3(0.5f, npc ? 0.6f : 0.5f, 0f), npc ? new Vector2(0.8f, 1.2f) : new Vector2(0.45f, 0.45f), 30);
                    nMarker++;
                }
                else if (makeMarkers && c == '~' && map.Get(x - 1, y) != '~')
                {
                    int len = Run(x, y, '~', area);
                    Box(markers, "[빛의 길] 선 60% 이상 발판", square, Hex("F9CB9C", 0.6f),
                        cell + new Vector3(len * 0.5f, 0.875f, 0f), new Vector2(len, 0.25f), 30);
                    nMarker++;
                }
            }
        }

        if (makeLabels)
        {
            foreach (Room r in map.rooms)
            {
                if (!area.Overlaps(r.rect)) continue;
                GameObject g = new GameObject("§ " + r.id + " " + r.name);
                g.transform.SetParent(labels, false);
                g.transform.position = new Vector3(r.rect.xMin + 0.3f + offset.x, r.rect.yMax - 0.6f + offset.y, 0f);
            }
            foreach (MapLabel l in map.labels)
            {
                if (!area.Contains(new Vector2Int(l.x, l.y))) continue;
                GameObject g = new GameObject("§ " + l.text);
                g.transform.SetParent(labels, false);
                g.transform.position = new Vector3(l.x + offset.x, l.y + 0.5f + offset.y, 0f);
            }
        }
        Debug.Log($"[세부 맵] 몬스터 {nEnemy}, 즉사 구역 {nKill}, 아이템 {nItem}, 기획 마커 {nMarker}");
    }

    int Run(int x, int y, char c, RectInt area)
    {
        int len = 0;
        while (x + len < area.xMax && map.Get(x + len, y) == c) len++;
        return len;
    }

    static Transform Child(Transform parent, string name)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        return g.transform;
    }

    static GameObject Box(Transform parent, string name, Sprite sprite, Color color, Vector3 center, Vector2 size, int order)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.position = center;
        g.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = g.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return g;
    }

    /// <summary>세부 맵 루트 제거. title이 null이면 전부.</summary>
    static void RemoveRoots(string title)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (!g.name.StartsWith(RootPrefix)) continue;
            if (title != null && g.name != RootPrefix + title) continue;
            Undo.DestroyObjectImmediate(g);
        }
    }

    static void RemoveForeignEnemies()
    {
        foreach (EnemyAI e in FindAll<EnemyAI>())
        {
            if (e == null) continue;
            Transform top = e.transform.root;
            if (top != null && top.name.StartsWith(RootPrefix)) continue;
            Undo.DestroyObjectImmediate(e.gameObject);
        }
    }

    void PlacePlayerAndCamera(Chapter c)
    {
        Vector3 pos = new Vector3(c.start.x + 0.5f + offset.x, c.start.y + offset.y, 0f);
        PlayerController pc = FindFirst<PlayerController>();
        if (pc != null)
        {
            Undo.RecordObject(pc.transform, "세부 맵 플레이어");
            pc.transform.position = pos;
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            Undo.RecordObject(cam.transform, "세부 맵 카메라");
            cam.transform.position = new Vector3(pos.x, pos.y + 1.5f, cam.transform.position.z);
        }
    }

    static void ApplyAbilities(Chapter c)
    {
        GameManager gm = FindFirst<GameManager>();
        if (gm == null) return;
        Undo.RecordObject(gm, "세부 맵 능력");
        gm.hasWallJump = c.wall;
        gm.hasDoubleJump = c.dbl;
        gm.hasDash = c.dash;
        EditorUtility.SetDirty(gm);
    }

    static void RecordTilemaps(LevelTilemap lt, string name)
    {
        var list = new List<Object> { lt.Source };
        foreach (Tilemap t in new[] { lt.solidLayer, lt.oneWayLayer, lt.spikeLayer, lt.updraftLayer, lt.savePointLayer, lt.noClimbLayer, lt.pullLayer })
            if (t != null) list.Add(t);
        Undo.RegisterCompleteObjectUndo(list.ToArray(), name);
    }

    static void RefreshColliders(LevelTilemap lt)
    {
        foreach (Tilemap t in new[] { lt.solidLayer, lt.oneWayLayer, lt.spikeLayer, lt.savePointLayer, lt.noClimbLayer })
        {
            if (t == null) continue;
            TilemapCollider2D tc = t.GetComponent<TilemapCollider2D>();
            if (tc != null)
            {
                var m = tc.GetType().GetMethod("ProcessTilemapChanges");
                if (m != null) m.Invoke(tc, null);
            }
            CompositeCollider2D cc = t.GetComponent<CompositeCollider2D>();
            if (cc != null) cc.GenerateGeometry();
        }
    }

    static TileBase Load(string name) => AssetDatabase.LoadAssetAtPath<TileBase>($"{TileDir}/{name}.asset");

    static T FindFirst<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindFirstObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }

    static T[] FindAll<T>() where T : Object
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
        return Object.FindObjectsOfType<T>();
#endif
    }

    static Color Hex(string h, float a = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + h, out Color c);
        c.a = a;
        return c;
    }

    // ───────────────────────── 데이터 ─────────────────────────
    public class Chapter
    {
        public string key, name;
        public RectInt box;
        public Vector2Int start;
        public bool wall, dbl, dash;
        public float windup;
    }

    public class Room
    {
        public string id, chapter, name;
        public RectInt rect;
    }

    public class MapLabel
    {
        public int x, y;
        public string text;
    }

    public class MapData
    {
        public int x0, y0, w, h;
        char[,] cells;
        public readonly List<Chapter> chapters = new List<Chapter>();
        public readonly List<Room> rooms = new List<Room>();
        public readonly List<MapLabel> labels = new List<MapLabel>();

        public char Get(int x, int y)
        {
            int i = x - x0, j = y - y0;
            if (i < 0 || j < 0 || i >= w || j >= h) return '.';
            return cells[i, j];
        }

        public float WindupAt(int x, int y)
        {
            foreach (Room r in rooms)
            {
                if (!r.rect.Contains(new Vector2Int(x, y))) continue;
                foreach (Chapter c in chapters) if (c.key == r.chapter) return c.windup;
            }
            return 0.4f;
        }

        public static MapData Parse(string text)
        {
            var m = new MapData();
            var ci = CultureInfo.InvariantCulture;
            bool sized = false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Length == 0 || line.StartsWith("#")) continue;
                int sp = line.IndexOf(' ');
                if (sp < 0) continue;
                string tag = line.Substring(0, sp), rest = line.Substring(sp + 1);
                switch (tag)
                {
                    case "SIZE":
                    {
                        string[] p = rest.Split(' ');
                        m.x0 = int.Parse(p[0], ci); m.y0 = int.Parse(p[1], ci);
                        m.w = int.Parse(p[2], ci); m.h = int.Parse(p[3], ci);
                        m.cells = new char[m.w, m.h];
                        for (int i = 0; i < m.w; i++) for (int j = 0; j < m.h; j++) m.cells[i, j] = '.';
                        sized = true;
                        break;
                    }
                    case "CHAPTER":
                    {
                        string[] p = rest.Split('|');
                        int ax = int.Parse(p[2], ci), bx = int.Parse(p[3], ci), ay = int.Parse(p[4], ci), by = int.Parse(p[5], ci);
                        m.chapters.Add(new Chapter
                        {
                            key = p[0], name = p[1],
                            box = new RectInt(ax, ay, bx - ax, by - ay),
                            start = new Vector2Int(int.Parse(p[6], ci), int.Parse(p[7], ci)),
                            wall = p[8] == "1", dbl = p[9] == "1", dash = p[10] == "1",
                            windup = float.Parse(p[11], ci),
                        });
                        break;
                    }
                    case "ROOM":
                    {
                        string[] p = rest.Split('|');
                        int ax = int.Parse(p[3], ci), bx = int.Parse(p[4], ci), ay = int.Parse(p[5], ci), by = int.Parse(p[6], ci);
                        m.rooms.Add(new Room { id = p[0], chapter = p[1], name = p[2], rect = new RectInt(ax, ay, bx - ax, by - ay) });
                        break;
                    }
                    case "LABEL":
                    {
                        string[] p = rest.Split(new[] { '|' }, 3);
                        m.labels.Add(new MapLabel { x = int.Parse(p[0], ci), y = int.Parse(p[1], ci), text = p[2] });
                        break;
                    }
                    case "ROW":
                    {
                        if (!sized) throw new Exception("SIZE 줄이 ROW보다 먼저 있어야 합니다");
                        int sp2 = rest.IndexOf(' ');
                        int y = int.Parse(rest.Substring(0, sp2), ci);
                        string row = rest.Substring(sp2 + 1);
                        int j = y - m.y0;
                        if (j < 0 || j >= m.h) break;
                        for (int i = 0; i < row.Length && i < m.w; i++) m.cells[i, j] = row[i];
                        break;
                    }
                }
            }
            if (!sized) throw new Exception("SIZE 줄이 없습니다");
            return m;
        }
    }
}

/// <summary>세부 맵 방 이름·마커를 씬 뷰에 글자로 표시.</summary>
[InitializeOnLoad]
static class WorldMapSceneLabels
{
    static GUIStyle roomStyle, markerStyle;

    static WorldMapSceneLabels()
    {
        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
    }

    static void OnScene(SceneView view)
    {
        if (!EditorPrefs.GetBool(WorldMapEditor.LabelPrefKey, true)) return;
        if (roomStyle == null)
        {
            roomStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            roomStyle.normal.textColor = new Color(0.55f, 0.85f, 1f);
            markerStyle = new GUIStyle(EditorStyles.miniLabel);
            markerStyle.normal.textColor = Color.white;
        }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (!root.name.StartsWith(WorldMapEditor.RootPrefix)) continue;
            Transform labels = root.transform.Find("라벨");
            if (labels != null)
                foreach (Transform t in labels)
                    Handles.Label(t.position, t.name.Substring(2), roomStyle);
            Transform markers = root.transform.Find("기획 마커");
            if (markers != null && view.camera != null && view.camera.orthographicSize < 25f)
                foreach (Transform t in markers)
                    Handles.Label(t.position + Vector3.up * 0.8f, t.name, markerStyle);
        }
    }
}
#endif

#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// 기획서 「선」 레벨 디자인 1·2·3단계 초안을 현재 씬의 Level 타일맵에 그려주는 에디터.
/// 메뉴: Tools > Metroidvania > 레벨 초안 에디터
///
/// - 타일(벽/붙을 수 없는 벽/발판/가시/상승기류/끌림/쉼터), 몬스터, 플레이어 시작 위치, 능력 상태를 자동 배치
/// - 프로토타입에 아직 없는 요소(아이템, 단서, NPC, 빛의 길, 출구)는
///   색 사각형 + 씬 뷰 라벨로 표시되는 "기획 마커"로 배치 (게임 동작 없음)
/// - 좌표 1칸 = 1m. 기획서의 공간 스케치 이미지와 같은 데이터로 생성됨
/// </summary>
public class LevelDraftEditor : EditorWindow
{
    const string TileDir = "Assets/_Project/Tiles";
    const string EnemyPrefabPath = "Assets/_Project/Prefabs/Enemy.prefab";
    const string SquarePath = "Assets/_Project/Art/Square.png";
    public const string MarkerRootName = "[레벨 초안] 기획 마커";
    const string EnemyRootName = "[레벨 초안] 몬스터";
    public const string LabelPrefKey = "LevelDraft.ShowLabels";

    bool placeEnemies = true;
    bool applyAbilities = true;
    bool placeMarkers = true;
    bool frameSceneView = true;

    [MenuItem("Tools/Metroidvania/레벨 초안 에디터")]
    static void Open() => GetWindow<LevelDraftEditor>("레벨 초안");

    void OnGUI()
    {
        EditorGUILayout.LabelField("「선」 레벨 디자인 초안", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "버튼을 누르면 현재 씬의 Level 타일맵을 지우고 해당 단계를 그립니다.\n" +
            "몬스터(EnemyAI)와 이전 초안 마커도 함께 교체됩니다. 실행 전 씬 저장을 권장합니다.",
            MessageType.Info);

        placeEnemies = EditorGUILayout.Toggle("몬스터 배치", placeEnemies);
        placeMarkers = EditorGUILayout.Toggle("기획 마커 배치", placeMarkers);
        applyAbilities = EditorGUILayout.Toggle("시작 능력 상태 적용", applyAbilities);
        frameSceneView = EditorGUILayout.Toggle("씬 뷰를 맵 전체에 맞춤", frameSceneView);

        bool labels = EditorPrefs.GetBool(LabelPrefKey, true);
        bool newLabels = EditorGUILayout.Toggle("씬 뷰 라벨 표시", labels);
        if (newLabels != labels) { EditorPrefs.SetBool(LabelPrefKey, newLabels); SceneView.RepaintAll(); }

        EditorGUILayout.Space();
        if (GUILayout.Button("1단계 · 요람에서 골목까지 (튜토리얼)", GUILayout.Height(30))) Draw(Stage1());
        if (GUILayout.Button("2단계 · 골목에서 도시로 (중간)", GUILayout.Height(30))) Draw(Stage2());
        if (GUILayout.Button("3단계 · 황혼의 회상 (어려움)", GUILayout.Height(30))) Draw(Stage3());

        EditorGUILayout.Space();
        if (GUILayout.Button("초안 지우기")) ClearAll();

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "능력 획득 마커(노란 마름모)에 도착하면 GameManager에서 해당 능력을 직접 ON 하세요.\n" +
            "1단계 시작: 모두 OFF / 2단계 시작: 벽타기 ON / 3단계 시작: 모두 ON",
            MessageType.None);
    }

    // ───────────────────────── 데이터 구조 ─────────────────────────
    public enum MarkerKind { Ability, Heal, Clue, Npc, Pull, Exit, Good, Back, Light }

    struct Marker
    {
        public MarkerKind kind; public int x, y, width; public string label;
        public Marker(MarkerKind k, int x, int y, string label, int width = 1)
        { kind = k; this.x = x; this.y = y; this.label = label; this.width = width; }
    }

    struct Section
    {
        public int x, y; public string label;
        public Section(int x, int y, string label) { this.x = x; this.y = y; this.label = label; }
    }

    class Stage
    {
        public string title;
        public RectInt bounds;
        public Vector2Int player;
        public bool wall, doubleJump, dash;
        public float windup;
        public Action<Painter> paint;
        public readonly List<Vector2Int> enemies = new List<Vector2Int>();
        public readonly List<Marker> markers = new List<Marker>();
        public readonly List<Section> sections = new List<Section>();
    }

    class Painter
    {
        public Tilemap tm;
        public TileBase ground, platform, spike, updraft, save, pull, noClimb;

        void Fill(TileBase t, int x0, int x1, int y0, int y1)
        {
            for (int x = x0; x <= x1; x++)
                for (int y = y0; y <= y1; y++)
                    tm.SetTile(new Vector3Int(x, y, 0), t);
        }
        public void Solid(int x0, int x1, int y0, int y1) => Fill(ground, x0, x1, y0, y1);
        public void Clear(int x0, int x1, int y0, int y1) => Fill(null, x0, x1, y0, y1);
        public void Spike(int x0, int x1, int y0, int y1) => Fill(spike, x0, x1, y0, y1);
        public void Platform(int x0, int x1, int y) => Fill(platform, x0, x1, y, y);
        public void Updraft(int x, int y) => tm.SetTile(new Vector3Int(x, y, 0), updraft);
        public void Save(int x, int y) => tm.SetTile(new Vector3Int(x, y, 0), save);
        public void Pull(int x, int y) { if (pull != null) tm.SetTile(new Vector3Int(x, y, 0), pull); }
        public void NoClimb(int x0, int x1, int y0, int y1) => Fill(noClimb != null ? noClimb : ground, x0, x1, y0, y1);
    }

    // ───────────────────────── 그리기 ─────────────────────────
    void Draw(Stage s)
    {
        LevelTilemap lt = FindFirst<LevelTilemap>();
        if (lt == null)
        {
            EditorUtility.DisplayDialog("레벨 초안",
                "씬에 LevelTilemap(Level 타일맵)이 없습니다.\n" +
                "Tools > Metroidvania > 현재 씬을 단일 타일맵(Level) 방식으로 변환 을 먼저 실행하세요.", "확인");
            return;
        }
        if (!EditorUtility.DisplayDialog("레벨 초안", s.title + "\n\n현재 Level 타일과 몬스터를 지우고 그립니다. 계속할까요?", "그리기", "취소"))
            return;

        Painter p = new Painter
        {
            tm = lt.Source,
            ground = Load("T_Ground"), platform = Load("T_Platform"), spike = Load("T_Spike"),
            updraft = Load("T_Updraft"), save = Load("T_SavePoint"),
            pull = Load("T_Pull"), noClimb = Load("T_WallNoClimb"),
        };
        if (p.ground == null || p.platform == null || p.spike == null || p.updraft == null || p.save == null)
        {
            EditorUtility.DisplayDialog("레벨 초안", TileDir + " 에서 T_ 타일 5종을 찾지 못했습니다.", "확인");
            return;
        }
        if (p.pull == null || p.noClimb == null || lt.pullLayer == null || lt.noClimbLayer == null)
            Debug.LogWarning("[레벨 초안] 끌림/붙을 수 없는 벽 타일 또는 레이어가 없습니다. " +
                             "Tools > Metroidvania > 기능 업데이트 적용 을 먼저 실행하세요. (끌림은 생략, 붙을 수 없는 벽은 일반 벽으로 그림)");

        RecordTilemaps(lt, "레벨 초안 그리기");
        lt.Source.ClearAllTiles();
        s.paint(p);
        lt.Source.CompressBounds();
        lt.Sync();
        RefreshColliders(lt);
        PullTilemap pullMap = lt.pullLayer != null ? lt.pullLayer.GetComponent<PullTilemap>() : null;
        if (pullMap != null) pullMap.Rebuild();

        RemoveDraftObjects();
        if (placeEnemies) SpawnEnemies(s);
        if (placeMarkers) SpawnMarkers(s);
        PlacePlayerAndCamera(s);
        if (applyAbilities) ApplyAbilities(s);

        if (frameSceneView && SceneView.lastActiveSceneView != null)
        {
            Vector3 center = new Vector3(s.bounds.center.x, s.bounds.center.y, 0f);
            SceneView.lastActiveSceneView.Frame(new Bounds(center, new Vector3(s.bounds.width, s.bounds.height, 1f)), false);
        }

        EditorSceneManager.MarkSceneDirty(lt.gameObject.scene);
        Debug.Log("[레벨 초안] " + s.title + " 생성 완료");
    }

    void ClearAll()
    {
        LevelTilemap lt = FindFirst<LevelTilemap>();
        if (!EditorUtility.DisplayDialog("레벨 초안", "Level 타일, 몬스터, 초안 마커를 모두 지울까요?", "지우기", "취소")) return;
        if (lt != null)
        {
            RecordTilemaps(lt, "레벨 초안 지우기");
            lt.Source.ClearAllTiles();
            lt.Sync();
            RefreshColliders(lt);
            EditorSceneManager.MarkSceneDirty(lt.gameObject.scene);
        }
        RemoveDraftObjects();
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

    static void RemoveDraftObjects()
    {
        foreach (EnemyAI e in FindAll<EnemyAI>())
            if (e != null) Undo.DestroyObjectImmediate(e.gameObject);
        foreach (string n in new[] { MarkerRootName, EnemyRootName })
        {
            GameObject go = GameObject.Find(n);
            if (go != null) Undo.DestroyObjectImmediate(go);
        }
    }

    void SpawnEnemies(Stage s)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null) { Debug.LogWarning("[레벨 초안] Enemy 프리팹을 찾지 못해 몬스터를 배치하지 않았습니다."); return; }

        GameObject root = new GameObject(EnemyRootName);
        Undo.RegisterCreatedObjectUndo(root, "레벨 초안 몬스터");
        foreach (Vector2Int c in s.enemies)
        {
            GameObject e = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            e.transform.position = new Vector3(c.x + 0.5f, c.y, 0f);
            EnemyAI ai = e.GetComponent<EnemyAI>();
            if (ai != null)
            {
                ai.attackWindup = s.windup;                 // 단계별 예비동작 (1단계 0.5 / 2단계 0.4 / 3단계 0.3)
                PrefabUtility.RecordPrefabInstancePropertyModifications(ai);
            }
        }
    }

    void SpawnMarkers(Stage s)
    {
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(SquarePath);
        GameObject root = new GameObject(MarkerRootName);
        root.tag = "EditorOnly";                            // 빌드에는 포함되지 않음
        Undo.RegisterCreatedObjectUndo(root, "레벨 초안 마커");

        foreach (Marker m in s.markers)
        {
            GameObject go = new GameObject("[" + KindName(m.kind) + "] " + m.label);
            go.transform.SetParent(root.transform, false);
            SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = square;
            sr.sortingOrder = 30;
            sr.color = KindColor(m.kind);

            switch (m.kind)
            {
                case MarkerKind.Light:
                    go.transform.position = new Vector3(m.x + m.width * 0.5f, m.y + 0.875f, 0f);
                    go.transform.localScale = new Vector3(m.width, 0.25f, 1f);
                    break;
                case MarkerKind.Exit:
                    go.transform.position = new Vector3(m.x + 0.5f, m.y + 1f, 0f);
                    go.transform.localScale = new Vector3(1f, 2f, 1f);
                    break;
                case MarkerKind.Pull:
                case MarkerKind.Good:
                case MarkerKind.Npc:
                    go.transform.position = new Vector3(m.x + 0.5f, m.y + 0.6f, 0f);
                    go.transform.localScale = new Vector3(0.8f, 1.2f, 1f);
                    break;
                case MarkerKind.Ability:
                    go.transform.position = new Vector3(m.x + 0.5f, m.y + 0.5f, 0f);
                    go.transform.rotation = Quaternion.Euler(0f, 0f, 45f);
                    go.transform.localScale = new Vector3(0.55f, 0.55f, 1f);
                    break;
                default:
                    go.transform.position = new Vector3(m.x + 0.5f, m.y + 0.5f, 0f);
                    go.transform.localScale = new Vector3(0.45f, 0.45f, 1f);
                    break;
            }
        }

        foreach (Section sec in s.sections)
        {
            GameObject go = new GameObject("§ " + sec.label);
            go.transform.SetParent(root.transform, false);
            go.transform.position = new Vector3(sec.x, sec.y, 0f);
        }
    }

    static void PlacePlayerAndCamera(Stage s)
    {
        Vector3 pos = new Vector3(s.player.x + 0.5f, s.player.y, 0f);
        PlayerController pc = FindFirst<PlayerController>();
        if (pc != null)
        {
            Undo.RecordObject(pc.transform, "레벨 초안 플레이어");
            pc.transform.position = pos;
        }
        Camera cam = Camera.main;
        if (cam != null)
        {
            Undo.RecordObject(cam.transform, "레벨 초안 카메라");
            cam.transform.position = new Vector3(pos.x, pos.y + 1.5f, cam.transform.position.z);
        }
    }

    static void ApplyAbilities(Stage s)
    {
        GameManager gm = FindFirst<GameManager>();
        if (gm == null) return;
        Undo.RecordObject(gm, "레벨 초안 능력");
        gm.hasWallJump = s.wall;
        gm.hasDoubleJump = s.doubleJump;
        gm.hasDash = s.dash;
        EditorUtility.SetDirty(gm);
    }

    // ───────────────────────── 유틸 ─────────────────────────
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

    public static string KindName(MarkerKind k)
    {
        switch (k)
        {
            case MarkerKind.Ability: return "능력";
            case MarkerKind.Heal: return "회복";
            case MarkerKind.Clue: return "단서";
            case MarkerKind.Npc: return "NPC";
            case MarkerKind.Pull: return "끌림";
            case MarkerKind.Exit: return "출구";
            case MarkerKind.Good: return "선몬스터";
            case MarkerKind.Back: return "통로";
            default: return "빛의 길";
        }
    }

    static Color KindColor(MarkerKind k)
    {
        switch (k)
        {
            case MarkerKind.Ability: return Hex("FFD54A");
            case MarkerKind.Heal: return Hex("9BE88A");
            case MarkerKind.Clue: return Hex("FFFFFF");
            case MarkerKind.Npc: return Hex("7EC8FF");
            case MarkerKind.Pull: return Hex("A066FF");
            case MarkerKind.Exit: return Hex("FFFFFF", 0.35f);
            case MarkerKind.Good: return Hex("DDDDDD");
            case MarkerKind.Back: return Hex("FFFFFF", 0.6f);
            default: return Hex("FFF4B0", 0.5f);
        }
    }

    static Color Hex(string h, float a = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + h, out Color c);
        c.a = a;
        return c;
    }

    // ───────────────────────── 단계 데이터 (기획서와 동일) ─────────────────────────
    static Stage Stage1()
    {
        var s = new Stage();
        s.title = "1단계 · 요람에서 골목까지 (영유아기 → 아동기)";
        s.bounds = new RectInt(-4, -6, 157, 41);
        s.player = new Vector2Int(1, 0);
        s.wall = false; s.doubleJump = false; s.dash = false; s.windup = 0.5f;
        s.paint = c =>
        {
            c.Solid(-4, 152, -6, -1);
            c.Solid(-4, -1, 0, 34);
            c.Solid(150, 152, 0, 34);
            c.Solid(-4, 152, 32, 34);
            c.Solid(-1, 39, 7, 31);
            c.Solid(12, 19, 1, 6);
            c.Solid(24, 26, 0, 0);
            c.Solid(29, 31, 0, 1);
            c.Clear(35, 37, -2, -1);
            c.Platform(41, 44, 1);
            c.Platform(46, 49, 3);
            c.Platform(51, 55, 5);
            c.Solid(56, 62, 0, 5);
            c.Spike(66, 67, -1, -1);
            c.Spike(72, 74, -1, -1);
            c.Solid(100, 100, 3, 15);
            c.Solid(104, 149, 0, 11);
            c.Save(3, 0);
            c.Save(106, 12);
            c.Platform(110, 113, 13);
            c.Platform(115, 118, 15);
            c.Solid(119, 121, 19, 19);
            c.Spike(120, 122, 11, 11);
            c.Solid(131, 131, 15, 25);
            c.Solid(135, 149, 12, 23);
        };
        s.enemies.Add(new Vector2Int(89, 0));
        s.enemies.Add(new Vector2Int(126, 12));
        s.markers.Add(new Marker(MarkerKind.Clue, 21, 1, "단서: 터널 너머의 빛"));
        s.markers.Add(new Marker(MarkerKind.Clue, 59, 6, "흔적 (선 +2)"));
        s.markers.Add(new Marker(MarkerKind.Heal, 78, 0, "따뜻한 기억 (+30)"));
        s.markers.Add(new Marker(MarkerKind.Ability, 97, 0, "붙잡기 획득 → 벽타기 ON"));
        s.markers.Add(new Marker(MarkerKind.Clue, 120, 20, "흔적 (도약 필요·백트래킹)"));
        s.markers.Add(new Marker(MarkerKind.Clue, 133, 26, "단서: 위에서 새어 나오는 빛"));
        s.markers.Add(new Marker(MarkerKind.Exit, 144, 24, "출구 → 2단계"));
        s.sections.Add(new Section(0, 4, "1-A 요람"));
        s.sections.Add(new Section(12, 4, "1-B 기어가기"));
        s.sections.Add(new Section(23, 4, "1-C 첫 걸음"));
        s.sections.Add(new Section(41, 9, "1-D 선(線) 발판"));
        s.sections.Add(new Section(64, 4, "1-E 상처"));
        s.sections.Add(new Section(82, 4, "1-F 첫 만남"));
        s.sections.Add(new Section(95, 4, "1-G 붙잡기"));
        s.sections.Add(new Section(105, 16, "1-H 놀이터 입구"));
        s.sections.Add(new Section(108, 25, "1-I 놀이터"));
        s.sections.Add(new Section(124, 16, "1-J 두 번째 만남"));
        s.sections.Add(new Section(132, 28, "1-K 오르막 골목"));
        return s;
    }

    static Stage Stage2()
    {
        var s = new Stage();
        s.title = "2단계 · 골목에서 도시로 (청소년기 → 성년·중년기)";
        s.bounds = new RectInt(-4, -6, 237, 57);
        s.player = new Vector2Int(1, 0);
        s.wall = true; s.doubleJump = false; s.dash = false; s.windup = 0.4f;
        s.paint = c =>
        {
            c.Solid(-4, 232, -6, -1);
            c.Solid(-4, -1, 0, 50);
            c.Solid(230, 232, 0, 50);
            c.Solid(-4, 232, 48, 50);
            c.Clear(12, 16, -3, -1);
            c.Spike(12, 16, -4, -4);
            c.Clear(25, 30, -3, -1);
            c.Spike(25, 30, -4, -4);
            c.Spike(38, 41, -1, -1);
            c.Solid(46, 46, 3, 12);
            c.Solid(50, 57, 0, 9);
            c.Clear(65, 75, -5, -1);
            c.Spike(65, 75, -6, -6);
            c.Solid(78, 81, 0, 4);
            c.Solid(84, 87, 0, 9);
            c.Updraft(91, 0);
            c.Updraft(92, 0);
            c.Spike(90, 93, 11, 11);
            c.Platform(94, 97, 5);
            c.Solid(98, 104, 0, 7);
            c.Updraft(101, 8);
            c.Platform(102, 105, 13);
            c.Updraft(105, 14);
            c.Solid(106, 229, 0, 21);
            c.Platform(114, 118, 23);
            c.Platform(120, 124, 25);
            c.Solid(135, 138, 27, 27);
            c.Solid(146, 150, 22, 23);
            c.Spike(160, 162, 21, 21);
            c.Solid(168, 185, 24, 47);
            c.Clear(174, 176, 8, 21);
            c.Spike(174, 176, 7, 7);
            c.Solid(205, 205, 25, 40);
            c.Solid(209, 229, 22, 37);
            c.Save(3, 0);
            c.Save(108, 22);
            c.Save(190, 22);
            c.Pull(44, 0);
            c.Pull(152, 22);
        };
        s.enemies.Add(new Vector2Int(21, 0));
        s.enemies.Add(new Vector2Int(141, 22));
        s.enemies.Add(new Vector2Int(154, 22));
        s.enemies.Add(new Vector2Int(158, 22));
        s.markers.Add(new Marker(MarkerKind.Back, 1, 3, "← 1단계 골목 (백트래킹)"));
        s.markers.Add(new Marker(MarkerKind.Ability, 54, 10, "도약 획득 → 더블점프 ON"));
        s.markers.Add(new Marker(MarkerKind.Clue, 86, 10, "단서: 위로 흐르는 온기"));
        s.markers.Add(new Marker(MarkerKind.Npc, 136, 28, "NPC 구출 (선 +10, +40)"));
        s.markers.Add(new Marker(MarkerKind.Ability, 148, 24, "질주 획득 → 대쉬 ON"));
        s.markers.Add(new Marker(MarkerKind.Heal, 165, 22, "따뜻한 기억 (+30)"));
        s.markers.Add(new Marker(MarkerKind.Clue, 180, 22, "흔적 (선 +2)"));
        s.markers.Add(new Marker(MarkerKind.Exit, 222, 38, "출구 → 3단계"));
        s.markers.Add(new Marker(MarkerKind.Light, 126, 27, "빛의 길 (선 60%↑)", 8));
        s.sections.Add(new Section(5, 7, "2-A 청소년 골목"));
        s.sections.Add(new Section(45, 14, "2-B 도약"));
        s.sections.Add(new Section(60, 4, "2-C 도약의 시험"));
        s.sections.Add(new Section(88, 18, "2-D 온기 우물"));
        s.sections.Add(new Section(107, 34, "2-E 성년 도시"));
        s.sections.Add(new Section(166, 30, "2-F 질주 통로"));
        s.sections.Add(new Section(195, 44, "2-G 출구 통로"));
        return s;
    }

    static Stage Stage3()
    {
        var s = new Stage();
        s.title = "3단계 · 황혼의 회상 (노년기)";
        s.bounds = new RectInt(-4, -34, 207, 75);
        s.player = new Vector2Int(1, 20);
        s.wall = true; s.doubleJump = true; s.dash = true; s.windup = 0.3f;
        s.paint = c =>
        {
            c.Solid(-4, 202, -34, -31);
            c.Solid(-4, -1, -30, 40);
            c.Solid(200, 202, -30, 40);
            c.Solid(-4, 202, 38, 40);
            c.Solid(-1, 40, -30, 19);
            c.Spike(10, 23, 19, 19);
            c.Solid(41, 55, -30, 5);
            c.Spike(43, 45, 5, 5);
            c.Solid(50, 50, 9, 29);
            c.Spike(50, 50, 16, 19);
            c.Solid(56, 80, -30, 29);
            c.Spike(56, 56, 22, 25);
            c.Solid(65, 80, 31, 37);
            c.Spike(81, 81, 5, 15);
            c.Spike(90, 90, -6, 0);
            c.Solid(81, 199, -30, -21);
            c.Solid(91, 199, -8, 37);
            c.Spike(95, 120, -21, -21);
            c.Updraft(101, -20);
            c.Platform(104, 108, -15);
            c.Spike(130, 132, -21, -21);
            c.Save(3, 20);
            c.Save(84, -20);
            c.Pull(49, 6);
            c.Pull(79, 30);
            c.Pull(135, -20);
            c.NoClimb(40, 40, 6, 19);
            c.NoClimb(80, 80, -20, 29);
            c.NoClimb(80, 80, 31, 37);
            c.NoClimb(91, 91, -8, 37);
        };
        s.enemies.Add(new Vector2Int(26, 20));
        s.enemies.Add(new Vector2Int(123, -20));
        s.enemies.Add(new Vector2Int(127, -20));
        s.enemies.Add(new Vector2Int(53, 6));
        s.markers.Add(new Marker(MarkerKind.Clue, 6, 21, "단서: 창밖의 세 층위"));
        s.markers.Add(new Marker(MarkerKind.Clue, 40, 21, "단서: 아래로 이어진 빛 (믿음의 낙하)"));
        s.markers.Add(new Marker(MarkerKind.Npc, 60, 30, "NPC: 옛 친구 (선 +10, +40)"));
        s.markers.Add(new Marker(MarkerKind.Heal, 112, -14, "따뜻한 기억 (+30)"));
        s.markers.Add(new Marker(MarkerKind.Npc, 150, -20, "NPC: 마지막 화해"));
        s.markers.Add(new Marker(MarkerKind.Good, 170, -20, "선 몬스터 (악 80%↑ 엔딩)"));
        s.markers.Add(new Marker(MarkerKind.Exit, 190, -20, "마지막 선 · 엔딩 분기"));
        s.markers.Add(new Marker(MarkerKind.Light, 96, -16, "빛의 길 (선 60%↑)", 25));
        s.sections.Add(new Section(0, 25, "3-A 황혼의 방 · 도시의 기억"));
        s.sections.Add(new Section(41, 11, "3-B 골목의 기억"));
        s.sections.Add(new Section(60, 33, "3-C 요람의 기억"));
        s.sections.Add(new Section(82, 2, "긴 낙하"));
        s.sections.Add(new Section(92, -10, "3-D 마지막 선"));
        return s;
    }

}

/// <summary>씬 뷰에 기획 마커 이름과 구간 이름을 라벨로 표시</summary>
[InitializeOnLoad]
static class LevelDraftSceneLabels
{
    static GUIStyle markerStyle, sectionStyle;

    static LevelDraftSceneLabels()
    {
        SceneView.duringSceneGui -= OnScene;
        SceneView.duringSceneGui += OnScene;
    }

    static void OnScene(SceneView view)
    {
        if (!EditorPrefs.GetBool(LevelDraftEditor.LabelPrefKey, true)) return;
        GameObject root = GameObject.Find(LevelDraftEditor.MarkerRootName);
        if (root == null) return;

        if (markerStyle == null)
        {
            markerStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.LowerCenter };
            markerStyle.normal.textColor = Color.white;
            sectionStyle = new GUIStyle(EditorStyles.helpBox) { fontSize = 12, fontStyle = FontStyle.Bold };
            sectionStyle.normal.textColor = Color.white;
        }

        foreach (Transform t in root.transform)
        {
            if (t.name.StartsWith("§ "))
                Handles.Label(t.position, t.name.Substring(2), sectionStyle);
            else
                Handles.Label(t.position + Vector3.up * 1.1f, t.name, markerStyle);
        }
    }
}
#endif

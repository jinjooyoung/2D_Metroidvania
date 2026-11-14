#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

/// <summary>
/// 메뉴: Tools > Metroidvania > 프로젝트 초안 생성
/// 레이어, 사각형 스프라이트, 타일 5종, 타일 팔레트, 플레이어/몬스터 프리팹, 테스트 씬을 자동 생성.
/// (프리팹·씬 YAML은 GUID가 프로젝트마다 달라서, 직접 파일을 넣는 대신 에디터에서 생성하는 방식)
/// </summary>
public static class ProjectBuilder
{
    const string Root = "Assets/_Project";
    const string ArtDir = Root + "/Art";
    const string TileDir = Root + "/Tiles";
    const string PrefabDir = Root + "/Prefabs";
    const string SceneDir = Root + "/Scenes";
    const string PhysicsDir = Root + "/Physics";
    const string ScenePath = SceneDir + "/LevelDesignTest.unity";

    static readonly string[] RequiredLayers = { "Ground", "OneWayPlatform", "Player", "Enemy" };

    // 색으로만 구분하는 타일 / 엔티티
    static readonly Color GroundColor    = Hex("5A5F6E");          // 벽·바닥·천장
    static readonly Color PlatformColor  = Hex("C8A064");          // 통과 발판
    static readonly Color SpikeColor     = Hex("FF4D8D");          // 가시 함정
    static readonly Color UpdraftColor   = Hex("5FD8FF", 0.45f);   // 상승기류
    static readonly Color SaveColor      = Hex("FFFFFF", 0.9f);    // 저장 지점 (미방문 = 흰색, 방문 시 런타임에서 초록)
    static readonly Color NoClimbColor   = Hex("33363F");          // 붙을 수 없는 벽 (벽보다 어두움)
    static readonly Color PullColor      = Hex("C8A2FF");          // 끌림 기믹 (연보라)
    static readonly Color PlayerColor    = Hex("3D8BFF");
    static readonly Color EnemyColor     = Hex("E8483B");
    static readonly Color BackgroundColor = Hex("1B1E26");

    internal class TileSet { public Tile ground, platform, spike, updraft, savePoint, noClimb, pull; }

    static TileSet LoadTileSet()
    {
        return new TileSet
        {
            ground    = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_Ground.asset"),
            platform  = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_Platform.asset"),
            spike     = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_Spike.asset"),
            updraft   = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_Updraft.asset"),
            savePoint = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_SavePoint.asset"),
            noClimb   = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_WallNoClimb.asset"),
            pull      = AssetDatabase.LoadAssetAtPath<Tile>(TileDir + "/T_Pull.asset"),
        };
    }

    [MenuItem("Tools/Metroidvania/프로젝트 초안 생성 (레이어·타일·프리팹·씬)")]
    public static void BuildAll()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("Metroidvania 초안",
                "LevelDesignTest 씬이 이미 있습니다.\n다시 생성하면 씬(그려둔 타일 포함)과 프리팹을 덮어씁니다. 계속할까요?",
                "덮어쓰기", "취소"))
            return;

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!EnsureLayers()) return;
        EnsureFolders();

        Sprite square = CreateSprite("Square", 32, 32, new Vector2(0.5f, 0.5f));
        // 발판: 두께 0.25, 칸의 윗부분에 붙도록 피벗을 아래로 뺌
        Sprite platform = CreateSprite("Platform", 32, 8, new Vector2(0.5f, -1f));
        PhysicsMaterial2D noFriction = CreateNoFrictionMaterial();

        TileSet tiles = CreateTiles(square, platform);
        CreatePalette(tiles);

        GameObject playerPrefab = CreatePlayerPrefab(square, noFriction);
        GameObject enemyPrefab = CreateEnemyPrefab(square, noFriction);

        Physics2D.IgnoreLayerCollision(L("Player"), L("Enemy"), true);

        CreateScene(tiles, playerPrefab, enemyPrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.DisplayDialog("Metroidvania 초안", "생성 완료!\n" + ScenePath + " 씬이 열렸습니다.\nPlay를 눌러 테스트하세요.", "확인");
    }

    // ───────────────────────── 레이어 / 폴더 ─────────────────────────
    static bool EnsureLayers()
    {
        SerializedObject tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        SerializedProperty layers = tagManager.FindProperty("layers");

        foreach (string name in RequiredLayers)
        {
            bool exists = false;
            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == name) { exists = true; break; }
            if (exists) continue;

            bool added = false;
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty sp = layers.GetArrayElementAtIndex(i);
                if (string.IsNullOrEmpty(sp.stringValue)) { sp.stringValue = name; added = true; break; }
            }
            if (!added)
            {
                EditorUtility.DisplayDialog("레이어 부족", $"'{name}' 레이어를 추가할 빈 슬롯이 없습니다.", "확인");
                return false;
            }
        }
        tagManager.ApplyModifiedProperties();
        return true;
    }

    static int L(string name) => LayerMask.NameToLayer(name);

    static void EnsureFolders()
    {
        foreach (string d in new[] { ArtDir, TileDir, PrefabDir, SceneDir, PhysicsDir })
            Directory.CreateDirectory(d);
        AssetDatabase.Refresh();
    }

    // ───────────────────────── 스프라이트 / 머티리얼 ─────────────────────────
    static Sprite CreateSprite(string name, int w, int h, Vector2 pivot)
    {
        string path = $"{ArtDir}/{name}.png";
        if (!File.Exists(path))
        {
            Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            Color32[] px = new Color32[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
        }

        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Sprite;
        imp.spriteImportMode = SpriteImportMode.Single;
        imp.spritePixelsPerUnit = 32;                 // 32px = 1유닛 = 1m
        imp.filterMode = FilterMode.Point;
        imp.textureCompression = TextureImporterCompression.Uncompressed;
        imp.mipmapEnabled = false;

        TextureImporterSettings s = new TextureImporterSettings();
        imp.ReadTextureSettings(s);
        s.spriteAlignment = (int)SpriteAlignment.Custom;
        s.spritePivot = pivot;
        s.spriteMeshType = SpriteMeshType.FullRect;
        s.spriteGenerateFallbackPhysicsShape = true;
        imp.SetTextureSettings(s);
        imp.SaveAndReimport();

        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static PhysicsMaterial2D CreateNoFrictionMaterial()
    {
        string path = PhysicsDir + "/NoFriction.physicsMaterial2D";
        PhysicsMaterial2D mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
        if (mat == null)
        {
            mat = new PhysicsMaterial2D("NoFriction");
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.friction = 0f;       // 벽에 달라붙는 현상 방지
        mat.bounciness = 0f;
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ───────────────────────── 타일 / 팔레트 ─────────────────────────
    static TileSet CreateTiles(Sprite square, Sprite platform)
    {
        return new TileSet
        {
            ground    = CreateTile("T_Ground",    square,   GroundColor,   Tile.ColliderType.Grid),
            platform  = CreateTile("T_Platform",  platform, PlatformColor, Tile.ColliderType.Sprite),
            spike     = CreateTile("T_Spike",     square,   SpikeColor,    Tile.ColliderType.Grid),
            updraft   = CreateTile("T_Updraft",   square,   UpdraftColor,  Tile.ColliderType.None),
            savePoint = CreateTile("T_SavePoint", square,   SaveColor,     Tile.ColliderType.Grid, TileFlags.None),
            noClimb   = CreateTile("T_WallNoClimb", square, NoClimbColor,  Tile.ColliderType.Grid),
            pull      = CreateTile("T_Pull",      square,   PullColor,     Tile.ColliderType.None),
        };
    }

    static Tile CreateTile(string name, Sprite sprite, Color color, Tile.ColliderType colliderType, TileFlags flags = TileFlags.LockColor)
    {
        string path = $"{TileDir}/{name}.asset";
        Tile t = AssetDatabase.LoadAssetAtPath<Tile>(path);
        if (t == null)
        {
            t = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(t, path);
        }
        t.sprite = sprite;
        t.color = color;
        t.colliderType = colliderType;
        t.flags = flags;   // 저장 지점은 런타임에 칸별 색을 바꾸므로 잠그지 않음
        EditorUtility.SetDirty(t);
        return t;
    }

    /// <summary>Tiles 폴더의 팔레트를 찾아 빠진 타일만 추가. 팔레트가 하나도 없으면 새로 만듦</summary>
    static void CreatePalette(TileSet tiles)
    {
        List<string> palettes = FindPalettes();
        if (palettes.Count > 0)
        {
            foreach (string pal in palettes) AddMissingTilesToPalette(pal, tiles);
            return;
        }
        CreateNewPalette(tiles);
    }

    static Tile[] AllTiles(TileSet t) =>
        new[] { t.ground, t.platform, t.spike, t.updraft, t.savePoint, t.noClimb, t.pull };

    /// <summary>Tiles 폴더 안의 타일 팔레트 프리팹 경로 (GridPalette 하위 에셋이 있는 프리팹)</summary>
    internal static List<string> FindPalettes()
    {
        var result = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { TileDir }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                if (o != null && o.GetType().Name == "GridPalette") { result.Add(path); break; }
        }
        return result;
    }

    internal static bool PaletteMissingTiles(string path, TileSet t)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        Tilemap tm = asset != null ? asset.GetComponentInChildren<Tilemap>() : null;
        if (tm == null) return false;
        foreach (Tile tile in AllTiles(t))
            if (tile != null && !tm.ContainsTile(tile)) return true;
        return false;
    }

    static void AddMissingTilesToPalette(string path, TileSet t)
    {
        if (!PaletteMissingTiles(path, t)) return;

        // 팔레트 설정(GridPalette) 하위 에셋이 저장 중 사라지는 경우를 대비해 기억
        bool hadSettings = HasPaletteSettings(path);

        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Tilemap tm = root.GetComponentInChildren<Tilemap>();
            if (tm == null) return;
            tm.CompressBounds();
            BoundsInt b = tm.cellBounds;
            int x = b.size.x > 0 ? b.xMax : 0;
            int y = b.size.y > 0 ? b.yMin : 0;
            foreach (Tile tile in AllTiles(t))
                if (tile != null && !tm.ContainsTile(tile)) tm.SetTile(new Vector3Int(x++, y, 0), tile);
            tm.RefreshAllTiles();
            PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        if (hadSettings && !HasPaletteSettings(path)) AddPaletteSettings(path);
        Debug.Log("[ProjectBuilder] 팔레트에 새 타일 추가: " + path);
    }

    static bool HasPaletteSettings(string path)
    {
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
            if (o != null && o.GetType().Name == "GridPalette") return true;
        return false;
    }

    static void AddPaletteSettings(string path)
    {
        Type gpType = FindType("UnityEditor.GridPalette");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (gpType == null || prefab == null) return;
        ScriptableObject gp = ScriptableObject.CreateInstance(gpType);
        gp.name = "Palette Settings";
        AssetDatabase.AddObjectToAsset(gp, prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(path);
    }

    static void CreateNewPalette(TileSet tiles)
    {
        string path = TileDir + "/LevelPalette.prefab";
        if (File.Exists(path)) AssetDatabase.DeleteAsset(path);

        GameObject go = new GameObject("LevelPalette");
        go.AddComponent<Grid>();
        GameObject layer = new GameObject("Layer1");
        layer.transform.SetParent(go.transform, false);
        Tilemap tm = layer.AddComponent<Tilemap>();
        layer.AddComponent<TilemapRenderer>();

        tm.SetTile(new Vector3Int(0, 0, 0), tiles.ground);
        tm.SetTile(new Vector3Int(1, 0, 0), tiles.platform);
        tm.SetTile(new Vector3Int(2, 0, 0), tiles.spike);
        tm.SetTile(new Vector3Int(3, 0, 0), tiles.updraft);
        tm.SetTile(new Vector3Int(4, 0, 0), tiles.savePoint);
        if (tiles.noClimb != null) tm.SetTile(new Vector3Int(5, 0, 0), tiles.noClimb);
        if (tiles.pull != null) tm.SetTile(new Vector3Int(6, 0, 0), tiles.pull);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        // GridPalette 타입은 패키지 버전에 따라 위치가 달라 리플렉션으로 생성
        Type gpType = FindType("UnityEditor.GridPalette");
        if (gpType != null && prefab != null)
        {
            ScriptableObject gp = ScriptableObject.CreateInstance(gpType);
            gp.name = "Palette Settings";
            AssetDatabase.AddObjectToAsset(gp, prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(path);
        }
        else
        {
            Debug.LogWarning("[ProjectBuilder] GridPalette 타입을 찾지 못했습니다. Tile Palette 창에서 새 팔레트를 만들고 Tiles 폴더의 타일 5개를 끌어다 놓으세요.");
        }
    }

    // ───────────────────────── 프리팹 ─────────────────────────
    static GameObject CreatePlayerPrefab(Sprite square, PhysicsMaterial2D mat)
    {
        GameObject root = new GameObject("Player");
        root.tag = "Player";
        root.layer = L("Player");

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.sleepMode = RigidbodySleepMode2D.NeverSleep;

        BoxCollider2D col = root.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.8f, 2f);
        col.offset = new Vector2(0f, 1f);       // 피벗 = 발바닥
        col.sharedMaterial = mat;

        Health health = root.AddComponent<Health>();
        health.maxHealth = 100;
        health.invincibleTime = 1f;

        PlayerController pc = root.AddComponent<PlayerController>();
        PlayerCombat combat = root.AddComponent<PlayerCombat>();

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        SpriteRenderer body = CreateSpriteChild("Body", visual, square, PlayerColor, new Vector2(0f, 1f), new Vector2(0.8f, 2f), 10);
        SpriteRenderer eye = CreateSpriteChild("Eye", visual, square, Color.white, new Vector2(0.22f, 1.65f), new Vector2(0.18f, 0.18f), 11);
        SpriteRenderer atk = CreateSpriteChild("AttackArea", visual, square, new Color(1f, 1f, 1f, 0f), new Vector2(1.4f, 1f), new Vector2(2f, 1f), 12);

        pc.visual = visual;
        pc.body = body.transform;
        pc.eye = eye.transform;
        pc.groundMask = LayerMask.GetMask("Ground");
        pc.oneWayMask = LayerMask.GetMask("OneWayPlatform");

        combat.attackFlash = atk;
        combat.targetMask = LayerMask.GetMask("Enemy");

        health.blinkRenderers = new[] { body, eye };

        return SavePrefab(root, PrefabDir + "/Player.prefab");
    }

    static GameObject CreateEnemyPrefab(Sprite square, PhysicsMaterial2D mat)
    {
        GameObject root = new GameObject("Enemy");
        root.layer = L("Enemy");

        Rigidbody2D rb = root.AddComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        rb.gravityScale = 3f;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;

        BoxCollider2D col = root.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.9f, 1.4f);
        col.offset = new Vector2(0f, 0.7f);
        col.sharedMaterial = mat;

        Health health = root.AddComponent<Health>();
        health.maxHealth = 30;
        health.invincibleTime = 0.1f;

        EnemyAI ai = root.AddComponent<EnemyAI>();

        Transform visual = new GameObject("Visual").transform;
        visual.SetParent(root.transform, false);
        SpriteRenderer body = CreateSpriteChild("Body", visual, square, EnemyColor, new Vector2(0f, 0.7f), new Vector2(0.9f, 1.4f), 8);
        SpriteRenderer eye = CreateSpriteChild("Eye", visual, square, Hex("FFE14D"), new Vector2(0.22f, 1.1f), new Vector2(0.18f, 0.18f), 9);
        SpriteRenderer atk = CreateSpriteChild("AttackArea", visual, square, new Color(1f, 0.1f, 0.1f, 0f), ai.attackOffset, ai.attackSize, 7);

        ai.visual = visual;
        ai.body = body;
        ai.attackArea = atk;
        ai.playerMask = LayerMask.GetMask("Player");
        ai.groundMask = LayerMask.GetMask("Ground");
        ai.ledgeMask = LayerMask.GetMask("Ground", "OneWayPlatform");

        health.blinkRenderers = new[] { body, eye };

        return SavePrefab(root, PrefabDir + "/Enemy.prefab");
    }

    static SpriteRenderer CreateSpriteChild(string name, Transform parent, Sprite sprite, Color color, Vector2 pos, Vector2 scale, int order)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    // ───────────────────────── 씬 ─────────────────────────
    static void CreateScene(TileSet tiles, GameObject playerPrefab, GameObject enemyPrefab)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 카메라
        GameObject camGO = new GameObject("Main Camera");
        camGO.tag = "MainCamera";
        Camera cam = camGO.AddComponent<Camera>();
        cam.orthographic = true;
        cam.orthographicSize = 7f;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = BackgroundColor;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 100f;
        camGO.transform.position = new Vector3(0.5f, 1.5f, -10f);
        camGO.AddComponent<AudioListener>();
        CameraFollow follow = camGO.AddComponent<CameraFollow>();

        TryAddGlobalLight2D();

        // 게임 매니저 (능력 bool은 여기 인스펙터에서 토글)
        GameObject gmGO = new GameObject("GameManager");
        GameManager gm = gmGO.AddComponent<GameManager>();
        gmGO.AddComponent<DebugHUD>();

        // 그리드 + 단일 레벨 타일맵 (여기에만 그리면 됨) + [자동] 충돌·기능 레이어
        GameObject gridGO = new GameObject("Grid");
        gridGO.AddComponent<Grid>();
        Tilemap level = CreateTilemap(gridGO.transform, LevelName, "Default", 3, true);
        PaintTestLevel(tiles, level);
        SetupLevelLayers(gridGO.transform, level, tiles);

        // 플레이어
        GameObject playerGO = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
        playerGO.transform.position = new Vector3(0.5f, 0f, 0f);
        gm.player = playerGO.GetComponent<PlayerController>();
        follow.target = playerGO.transform;

        // 몬스터
        Transform enemies = new GameObject("Enemies").transform;
        foreach (Vector2 p in new[] { new Vector2(38.5f, 0f), new Vector2(44.5f, 0f), new Vector2(72.5f, 15f) })
        {
            GameObject e = (GameObject)PrefabUtility.InstantiatePrefab(enemyPrefab, enemies);
            e.transform.position = p;
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        AddSceneToBuildSettings(ScenePath);
    }

    const string LevelName = "Level (여기에 그리기)";

    static Tilemap CreateTilemap(Transform grid, string name, string layer, int order, bool withRenderer)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(grid, false);
        go.layer = L(layer);
        Tilemap tm = go.AddComponent<Tilemap>();
        if (withRenderer)
        {
            TilemapRenderer r = go.AddComponent<TilemapRenderer>();
            r.sortingOrder = order;
        }
        return tm;
    }

    /// <summary>[자동] 레이어 5개(렌더러 없음, 충돌/기능만)를 만들고 Level 타일맵과 연결</summary>
    /// <summary>[자동] 레이어(렌더러 없음, 충돌/기능만)를 없는 것만 만들고 Level 타일맵과 연결</summary>
    static LevelTilemap SetupLevelLayers(Transform grid, Tilemap level, TileSet t)
    {
        LevelTilemap lt = level.GetComponent<LevelTilemap>();
        if (lt == null) lt = level.gameObject.AddComponent<LevelTilemap>();

        if (lt.solidLayer == null)
        {
            lt.solidLayer = CreateTilemap(grid, "[자동] 충돌_벽바닥", "Ground", 0, false);
            MakeSolid(lt.solidLayer.gameObject, false);
        }
        if (lt.oneWayLayer == null)
        {
            lt.oneWayLayer = CreateTilemap(grid, "[자동] 충돌_발판", "OneWayPlatform", 0, false);
            MakeSolid(lt.oneWayLayer.gameObject, true);
        }
        if (lt.spikeLayer == null)
        {
            lt.spikeLayer = CreateTilemap(grid, "[자동] 충돌_가시", "Ground", 0, false);
            MakeSolid(lt.spikeLayer.gameObject, false);
            DamageOnTouch dmg = lt.spikeLayer.gameObject.AddComponent<DamageOnTouch>();
            dmg.damage = 10;
            dmg.targetMask = LayerMask.GetMask("Player");
        }
        if (lt.updraftLayer == null)
        {
            lt.updraftLayer = CreateTilemap(grid, "[자동] 상승기류", "Default", 0, false);
            lt.updraftLayer.gameObject.AddComponent<UpdraftTilemap>();
        }
        if (lt.savePointLayer == null)
        {
            lt.savePointLayer = CreateTilemap(grid, "[자동] 저장지점", "Default", 0, false);
            TilemapCollider2D saveCol = lt.savePointLayer.gameObject.AddComponent<TilemapCollider2D>();
            saveCol.isTrigger = true;
            lt.savePointLayer.gameObject.AddComponent<SavePointTilemap>();
        }
        if (lt.noClimbLayer == null)
        {
            lt.noClimbLayer = CreateTilemap(grid, "[자동] 충돌_붙을수없는벽", "Ground", 0, false);
            MakeSolid(lt.noClimbLayer.gameObject, false);
            WallSurface ws = lt.noClimbLayer.gameObject.AddComponent<WallSurface>();
            ws.climbable = false;
        }
        if (lt.pullLayer == null)
        {
            lt.pullLayer = CreateTilemap(grid, "[자동] 끌림", "Default", 0, false);
            lt.pullLayer.gameObject.AddComponent<PullTilemap>();
        }

        lt.solidTiles     = new TileBase[] { t.ground };
        lt.oneWayTiles    = new TileBase[] { t.platform };
        lt.spikeTiles     = new TileBase[] { t.spike };
        lt.updraftTiles   = new TileBase[] { t.updraft };
        lt.savePointTiles = new TileBase[] { t.savePoint };
        lt.noClimbTiles   = new TileBase[] { t.noClimb };
        lt.pullTiles      = new TileBase[] { t.pull };

        lt.Sync();
        RefreshColliders(lt.solidLayer, lt.oneWayLayer, lt.spikeLayer, lt.savePointLayer, lt.noClimbLayer);
        return lt;
    }

    // ───────────────────────── 기능 업데이트 (기존 프로젝트용) ─────────────────────────
    [MenuItem("Tools/Metroidvania/기능 업데이트 적용 (붙을 수 없는 벽·끌림 기믹·저장 지점)")]
    public static void ApplyFeatureUpdate()
    {
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Square.png");
        Sprite platform = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Platform.png");
        if (square == null || platform == null)
        {
            EditorUtility.DisplayDialog("기능 업데이트", "Art 폴더의 스프라이트를 찾지 못했습니다. '프로젝트 초안 생성'을 먼저 실행하세요.", "확인");
            return;
        }

        TileSet tiles = CreateTiles(square, platform);   // 새 타일 2종 생성(없을 때) + 저장 지점 타일 갱신
        CreatePalette(tiles);                              // 기존 팔레트에 빠진 타일 추가
        AssetDatabase.SaveAssets();

#if UNITY_2023_1_OR_NEWER
        LevelTilemap lt = Object.FindFirstObjectByType<LevelTilemap>();
#else
        LevelTilemap lt = Object.FindObjectOfType<LevelTilemap>();
#endif
        string msg = "타일 2종(붙을 수 없는 벽, 끌림)과 팔레트를 업데이트했습니다.";
        if (lt != null)
        {
            Grid grid = lt.GetComponentInParent<Grid>();
            SetupLevelLayers(grid != null ? grid.transform : lt.transform.parent, lt.Source, tiles);
            lt.Source.RefreshAllTiles();
            EditorSceneManager.MarkSceneDirty(lt.gameObject.scene);
            msg += "\n현재 씬의 Level에 [자동] 레이어 2개를 추가했습니다. 씬을 저장하세요 (Ctrl+S).";
        }
        else msg += "\n현재 씬에 Level 타일맵이 없어 레이어는 추가하지 않았습니다.";
        EditorUtility.DisplayDialog("기능 업데이트", msg, "확인");
    }

    // ───────────────────────── 기존 씬 변환 ─────────────────────────
    [MenuItem("Tools/Metroidvania/현재 씬을 단일 타일맵(Level) 방식으로 변환")]
    public static void ConvertCurrentScene()
    {
#if UNITY_2023_1_OR_NEWER
        Grid grid = Object.FindFirstObjectByType<Grid>();
#else
        Grid grid = Object.FindObjectOfType<Grid>();
#endif
        if (grid == null) { EditorUtility.DisplayDialog("변환", "씬에 Grid가 없습니다.", "확인"); return; }
        if (grid.GetComponentInChildren<LevelTilemap>() != null)
        {
            EditorUtility.DisplayDialog("변환", "이미 Level 방식으로 되어 있습니다.", "확인");
            return;
        }
        if (!EditorUtility.DisplayDialog("변환",
            "Grid 아래 모든 타일맵의 타일을 'Level' 타일맵 하나로 합치고,\n" +
            "기존 타일맵은 삭제한 뒤 [자동] 레이어로 다시 만듭니다.\n" +
            "(타일 종류는 그대로 보존되며, 종류에 맞게 자동 분배됩니다)\n\n" +
            "실행 전에 씬을 저장해 두는 걸 권장합니다. 진행할까요?", "변환", "취소"))
            return;

        TileSet t = LoadTileSet();
        if (t.ground == null || t.spike == null || t.platform == null || t.updraft == null || t.savePoint == null)
        {
            EditorUtility.DisplayDialog("변환", TileDir + " 에서 T_ 타일 5종을 찾지 못했습니다.\n'프로젝트 초안 생성'을 먼저 실행했는지 확인하세요.", "확인");
            return;
        }

        Tilemap[] old = grid.GetComponentsInChildren<Tilemap>(true);
        Tilemap level = CreateTilemap(grid.transform, LevelName, "Default", 3, true);

        // 같은 칸에 여러 타일이 겹치면 우선순위 높은 쪽 (가시 > 벽 > 발판 > 저장 > 기류 > 기타)
        System.Func<TileBase, int> priority = tile =>
            tile == t.spike ? 5 : (tile == t.ground || tile == t.noClimb) ? 4 : tile == t.platform ? 3 :
            tile == t.savePoint ? 2 : (tile == t.updraft || tile == t.pull) ? 1 : 0;

        int copied = 0;
        foreach (Tilemap tm in old)
        {
            tm.CompressBounds();
            foreach (Vector3Int p in tm.cellBounds.allPositionsWithin)
            {
                TileBase tile = tm.GetTile(p);
                if (tile == null) continue;
                TileBase cur = level.GetTile(p);
                if (cur == null || priority(tile) > priority(cur)) { level.SetTile(p, tile); copied++; }
            }
        }
        foreach (Tilemap tm in old) Object.DestroyImmediate(tm.gameObject);

        SetupLevelLayers(grid.transform, level, t);
        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        Selection.activeGameObject = level.gameObject;
        EditorUtility.DisplayDialog("변환", $"완료: 타일 {copied}개를 Level로 합쳤습니다.\nTile Palette의 Active Tilemap을 '{LevelName}'로 선택하고 그리세요.", "확인");
    }

    /// <summary>타일 콜라이더를 Composite로 합침 (이음새 걸림 방지). effector=true면 아래에서 통과 가능한 발판.</summary>
    static void MakeSolid(GameObject go, bool oneWay)
    {
        Rigidbody2D rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;

        TilemapCollider2D tc = go.AddComponent<TilemapCollider2D>();
        CompositeCollider2D cc = go.AddComponent<CompositeCollider2D>();
        cc.geometryType = CompositeCollider2D.GeometryType.Polygons;   // 내부 겹침 검사(머리 공간 체크)에 필요
#if UNITY_6000_0_OR_NEWER
        tc.compositeOperation = Collider2D.CompositeOperation.Merge;
#else
        tc.usedByComposite = true;
#endif
        if (oneWay)
        {
            cc.usedByEffector = true;
            PlatformEffector2D pe = go.AddComponent<PlatformEffector2D>();
            pe.useOneWay = true;
            pe.surfaceArc = 170f;
            pe.useSideFriction = false;
            pe.useSideBounce = false;
        }
    }

    static void RefreshColliders(params Tilemap[] maps)
    {
        foreach (Tilemap tm in maps)
        {
            TilemapCollider2D tc = tm.GetComponent<TilemapCollider2D>();
            if (tc != null)
            {
                var m = tc.GetType().GetMethod("ProcessTilemapChanges");
                if (m != null) m.Invoke(tc, null);
            }
            CompositeCollider2D cc = tm.GetComponent<CompositeCollider2D>();
            if (cc != null) cc.GenerateGeometry();
        }
    }

    // ───────────────────────── 테스트 레벨 ─────────────────────────
    static void PaintTestLevel(TileSet t, Tilemap level)
    {
        // 바닥 (윗면 y = 0), 좌우 벽
        Fill(level, t.ground, -12, 82, -4, -1);
        Fill(level, t.ground, -12, -10, 0, 22);
        Fill(level, t.ground, 80, 82, 0, 22);

        // ① 웅크리기 터널: 높이 1m 통로 (x 8~13)
        Fill(level, t.ground, 8, 13, 1, 3);

        // ② 가시 함정 3칸 (바닥 맨 윗줄을 가시로 교체)
        Fill(level, t.spike, 17, 19, -1, -1);

        // ③ 통과 발판 (윗면 높이 3m, 6m)
        Fill(level, t.platform, 22, 25, 2, 2);
        Fill(level, t.platform, 27, 30, 5, 5);

        // ④ 몬스터 구역 x 33~48 (평지)

        // ⑤ 상승기류 1칸 → 높이 6m 블록 위로
        Fill(level, t.updraft, 49, 49, 0, 0);
        Fill(level, t.ground, 51, 55, 0, 5);

        // ⑥ 벽타기 수직 통로 (폭 3칸, 높이 15m) → 위쪽 고지대
        Fill(level, t.ground, 59, 59, 3, 15);
        Fill(level, t.ground, 63, 79, 0, 14);

        // 저장 지점
        level.SetTile(new Vector3Int(2, 0, 0), t.savePoint);
        level.SetTile(new Vector3Int(31, 0, 0), t.savePoint);
        level.SetTile(new Vector3Int(66, 15, 0), t.savePoint);
    }

    static void Fill(Tilemap tm, TileBase tile, int x0, int x1, int y0, int y1)
    {
        for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
                tm.SetTile(new Vector3Int(x, y, 0), tile);
    }

    // ───────────────────────── 기타 ─────────────────────────
    static void AddSceneToBuildSettings(string path)
    {
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        list.RemoveAll(s => s.path == path);
        list.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
    }

    /// <summary>URP 2D 렌더러를 쓰는 프로젝트면 전역 2D 라이트 추가 (스프라이트가 어둡게 보이는 것 방지)</summary>
    static void TryAddGlobalLight2D()
    {
        Type lightType = FindType("UnityEngine.Rendering.Universal.Light2D");
        if (lightType == null) return;

        GameObject go = new GameObject("Global Light 2D");
        try
        {
            Component light = go.AddComponent(lightType);
            var prop = lightType.GetProperty("lightType");
            prop.SetValue(light, Enum.Parse(prop.PropertyType, "Global"));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[ProjectBuilder] Global Light 2D 설정 실패, 필요하면 직접 추가하세요: " + e.Message);
            Object.DestroyImmediate(go);
        }
    }

    static Type FindType(string fullName)
    {
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type t = asm.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    // ───────────────────────── 자동 설정 (에디터 로드 시) ─────────────────────────
    /// <summary>새 타일이 없거나 팔레트에 빠져 있으면 만들고, 씬의 Level에 새 레이어가 없으면 연결 여부를 물음</summary>
    internal static void AutoSetup(bool askScene)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        Sprite square = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Square.png");
        Sprite platform = AssetDatabase.LoadAssetAtPath<Sprite>(ArtDir + "/Platform.png");
        if (square == null || platform == null) return;          // 아직 초안 생성 전

        TileSet t = LoadTileSet();
        bool tilesMissing = t.noClimb == null || t.pull == null;
        if (tilesMissing) t = CreateTiles(square, platform);

        bool paletteChanged = false;
        foreach (string pal in FindPalettes())
            if (PaletteMissingTiles(pal, t)) { AddMissingTilesToPalette(pal, t); paletteChanged = true; }
        if (tilesMissing || paletteChanged)
        {
            AssetDatabase.SaveAssets();
            Debug.Log("[ProjectBuilder] 붙을 수 없는 벽 / 끌림 타일을 팔레트에 추가했습니다.");
        }

        if (!askScene) return;
#if UNITY_2023_1_OR_NEWER
        LevelTilemap lt = Object.FindFirstObjectByType<LevelTilemap>();
#else
        LevelTilemap lt = Object.FindObjectOfType<LevelTilemap>();
#endif
        if (lt == null) return;
        bool needsLayers = lt.noClimbLayer == null || lt.pullLayer == null
                           || lt.noClimbTiles == null || lt.noClimbTiles.Length == 0
                           || lt.pullTiles == null || lt.pullTiles.Length == 0;
        if (!needsLayers) return;

        string key = "MV.AskLayers." + lt.gameObject.scene.path;
        if (SessionState.GetBool(key, false)) return;
        SessionState.SetBool(key, true);

        if (EditorUtility.DisplayDialog("새 타일 기능 연결",
            "현재 씬의 Level 타일맵에 '붙을 수 없는 벽'과 '끌림' 기능 레이어가 없습니다.\n" +
            "지금 연결할까요? (연결하지 않으면 두 타일은 그려져도 기능이 동작하지 않습니다)", "연결", "나중에"))
        {
            Grid grid = lt.GetComponentInParent<Grid>();
            SetupLevelLayers(grid != null ? grid.transform : lt.transform.parent, lt.Source, t);
            lt.Source.RefreshAllTiles();
            EditorSceneManager.MarkSceneDirty(lt.gameObject.scene);
            Debug.Log("[ProjectBuilder] Level에 [자동] 레이어(붙을 수 없는 벽, 끌림)를 추가했습니다. 씬을 저장하세요.");
        }
    }

    static Color Hex(string hex, float alpha = 1f)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        c.a = alpha;
        return c;
    }
}

[InitializeOnLoad]
static class ProjectBuilderAutoSetup
{
    static ProjectBuilderAutoSetup()
    {
        EditorApplication.delayCall += () => ProjectBuilder.AutoSetup(true);
        EditorSceneManager.sceneOpened -= OnSceneOpened;
        EditorSceneManager.sceneOpened += OnSceneOpened;
    }

    static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
    {
        EditorApplication.delayCall += () => ProjectBuilder.AutoSetup(true);
    }
}
#endif

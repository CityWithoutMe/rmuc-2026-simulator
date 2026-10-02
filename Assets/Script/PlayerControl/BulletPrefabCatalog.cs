using UnityEngine;

// 运行时加载 17mm / 42mm。预制体仍在 Assets/prefabs/items，这里只存引用，Play 时用 Resources.Load。
[CreateAssetMenu(fileName = "BulletPrefabs", menuName = "Game/Bullet Prefabs")]
public class BulletPrefabCatalog : ScriptableObject
{
    public GameObject prefab17mm;
    public GameObject prefab42mm;

#if UNITY_EDITOR
    const string Path17 = "Assets/prefabs/items/bull_17mm.prefab";
    const string Path42 = "Assets/prefabs/items/bull_42mm.prefab";

    private void OnValidate()
    {
        if (prefab17mm == null)
            prefab17mm = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path17);
        if (prefab42mm == null)
            prefab42mm = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(Path42);
    }
#endif
}

# Plan: Nouveaux Outils MCP Unity Server

## État Actuel
- **70 outils existants** répartis en 12 fichiers Tools/*.cs
- Architecture: partial class `McpUnityServer` avec méthodes `static partial void Register*Tools()`

## Nouveaux Outils Proposés

### 1. NavMeshTools.cs (5 outils) - Navigation AI
| Outil | Description | API Unity |
|-------|-------------|-----------|
| `unity_bake_navmesh` | Bake le NavMesh de la scène | `NavMeshBuilder.BuildNavMesh()` |
| `unity_get_navmesh_settings` | Récupère les settings NavMesh | `NavMesh.GetSettingsByID()` |
| `unity_set_navmesh_settings` | Configure agent radius, height, slope | `NavMeshBuildSettings` |
| `unity_clear_navmesh` | Supprime le NavMesh | `NavMesh.RemoveAllNavMeshData()` |
| `unity_add_navmesh_surface` | Ajoute NavMeshSurface component | `NavMeshSurface` (AI Navigation package) |

### 2. BakingTools.cs (6 outils) - Lighting & Occlusion
| Outil | Description | API Unity |
|-------|-------------|-----------|
| `unity_bake_lighting` | Bake les lightmaps (sync) | `Lightmapping.Bake()` |
| `unity_bake_lighting_async` | Bake async avec progress | `Lightmapping.BakeAsync()` |
| `unity_get_lightmap_settings` | Récupère les settings | `LightmapEditorSettings` |
| `unity_set_lightmap_settings` | Configure quality, resolution | `LightmapEditorSettings` |
| `unity_clear_baked_data` | Supprime lightmaps | `Lightmapping.Clear()` |
| `unity_bake_occlusion` | Bake occlusion culling | `StaticOcclusionCulling.Compute()` |

### 3. BuildTools.cs (6 outils) - Build & AssetBundles
| Outil | Description | API Unity |
|-------|-------------|-----------|
| `unity_build_player` | Build le projet | `BuildPipeline.BuildPlayer()` |
| `unity_get_build_settings` | Récupère scenes, target | `EditorBuildSettings` |
| `unity_set_build_settings` | Configure scenes, target | `EditorBuildSettings.scenes` |
| `unity_build_assetbundle` | Build AssetBundles | `BuildPipeline.BuildAssetBundles()` |
| `unity_get_build_target` | Target platform actuel | `EditorUserBuildSettings.activeBuildTarget` |
| `unity_switch_platform` | Change de plateforme | `EditorUserBuildSettings.SwitchActiveBuildTarget()` |

### 4. PackageManagerTools.cs (5 outils) - Package Manager
| Outil | Description | API Unity |
|-------|-------------|-----------|
| `unity_list_packages` | Liste packages installés | `PackageManager.Client.List()` |
| `unity_add_package` | Ajoute un package | `PackageManager.Client.Add()` |
| `unity_remove_package` | Supprime un package | `PackageManager.Client.Remove()` |
| `unity_search_packages` | Recherche packages | `PackageManager.Client.SearchAll()` |
| `unity_get_package_info` | Info détaillée package | `PackageInfo` |

### 5. PhysicsTools.cs (4 outils) - Physics Baking
| Outil | Description | API Unity |
|-------|-------------|-----------|
| `unity_bake_mesh_colliders` | Bake mesh colliders | `MeshCollider.sharedMesh` |
| `unity_get_physics_settings` | Physics settings | `Physics.gravity`, etc. |
| `unity_set_physics_settings` | Configure physics | Layer collision matrix |
| `unity_simulate_physics` | Simulate physics steps | `Physics.Simulate()` |

## Résumé des Ajouts
| Catégorie | Nouveaux Outils |
|-----------|-----------------|
| NavMesh | 5 |
| Baking (Light/Occlusion) | 6 |
| Build | 6 |
| Package Manager | 5 |
| Physics | 4 |
| **Total** | **26 nouveaux outils** |

## Impact
- **Avant**: 70 outils
- **Après**: 96 outils
- **Fichiers à créer**: 5 nouveaux fichiers dans `Tools/`
- **Modifications**:
  - `McpUnityServer.cs`: Ajouter 5 déclarations partial void Register*Tools()
  - `README.md`: Mettre à jour documentation

## Priorité d'Implémentation
1. **BakingTools** - Lightmap baking (très demandé)
2. **NavMeshTools** - Navigation AI
3. **BuildTools** - Build automation
4. **PackageManagerTools** - Package management
5. **PhysicsTools** - Physics utilities

## Notes Techniques
- Utiliser `UnityEditor.AI` namespace pour NavMesh
- `Lightmapping.BakeAsync()` retourne un `AsyncOperation`
- `PackageManager.Client` utilise des `Request` async
- Build peut prendre du temps - prévoir timeout adapté

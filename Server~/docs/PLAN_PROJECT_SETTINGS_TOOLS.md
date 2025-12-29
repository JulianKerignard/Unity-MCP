# Plan: Outils Project Settings pour MCP Unity

## 1. Analyse de la Situation Actuelle

### État du Projet
- **52 outils MCP existants** répartis en 11 fichiers Tools/*.cs
- Architecture modulaire avec `partial class McpUnityServer`
- Pattern établi: `RegisterXxxTools()` pour chaque catégorie
- TagLayerTools.cs montre déjà l'accès aux ProjectSettings via SerializedObject

### Structure Existante des Tools
```
Tools/
├── AnimatorTools.cs    (16 outils)
├── AssetTools.cs       (5 outils)
├── ComponentTools.cs   (3 outils)
├── EditorTools.cs      (8 outils)
├── GameObjectTools.cs  (7 outils)
├── MaterialTools.cs    (3 outils)
├── MemoryTools.cs      (3 outils)
├── PrefabTools.cs      (4 outils)
├── SceneTools.cs       (4 outils)
├── TagLayerTools.cs    (6 outils)
└── UITools.cs          (4 outils)
```

### Objectif
Créer des outils pour manipuler les Project Settings Unity:
- Graphics/Quality Settings
- Physics Settings
- Time Settings
- Audio Settings
- Render/Lighting Settings
- Input Settings
- Player Settings

---

## 2. Recherches Effectuées

### Documentation Officielle Consultée

| API | Source | Accès |
|-----|--------|-------|
| [QualitySettings](https://docs.unity3d.com/ScriptReference/QualitySettings.html) | Unity Scripting API | Statique direct |
| [PlayerSettings](https://docs.unity3d.com/ScriptReference/PlayerSettings.html) | Unity Scripting API | Editor only |
| [Physics](https://docs.unity3d.com/ScriptReference/Physics.html) | Unity Scripting API | Statique direct |
| [RenderSettings](https://docs.unity3d.com/ScriptReference/RenderSettings.html) | Unity Scripting API | Statique direct |
| [Time](https://docs.unity3d.com/ScriptReference/Time.html) | Unity Scripting API | Statique direct |
| [AudioSettings](https://docs.unity3d.com/ScriptReference/AudioSettings.html) | Unity Scripting API | Runtime config |
| [GraphicsSettings](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsSettings.html) | Unity Scripting API | Rendering namespace |

### Méthodes d'Accès aux Settings

**1. API Statique Directe** (recommandé quand disponible):
```csharp
// QualitySettings
QualitySettings.SetQualityLevel(index);
QualitySettings.shadows = ShadowQuality.All;

// Physics
Physics.gravity = new Vector3(0, -9.81f, 0);

// Time
Time.timeScale = 1.0f;
Time.fixedDeltaTime = 0.02f;

// RenderSettings (per-scene)
RenderSettings.fog = true;
RenderSettings.ambientMode = AmbientMode.Skybox;
```

**2. SerializedObject** (pour settings sans API publique):
```csharp
// Accès aux fichiers ProjectSettings/*.asset
SerializedObject settings = new SerializedObject(
    AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]
);
SerializedProperty prop = settings.FindProperty("m_PropertyName");
```

**3. AudioConfiguration** (pour audio):
```csharp
AudioConfiguration config = AudioSettings.GetConfiguration();
config.sampleRate = 48000;
AudioSettings.Reset(config);
```

### Bonnes Pratiques Identifiées

1. **Utiliser l'API publique quand disponible** - Plus stable entre versions Unity
2. **Undo support** - Enregistrer les modifications pour annulation
3. **Validation** - Vérifier les valeurs avant application
4. **Per-scene vs Global** - RenderSettings sont par scène, QualitySettings sont globaux

---

## 3. Plan d'Action Détaillé

### Approche Recommandée: **Outils Génériques avec Catégories**

Plutôt que 50+ outils individuels, créer **6 outils principaux** avec sous-catégories:

| Outil | Description | Complexité |
|-------|-------------|------------|
| `unity_get_project_settings` | Lire tous les settings d'une catégorie | Moyenne |
| `unity_set_project_settings` | Modifier des settings | Moyenne |
| `unity_get_quality_level` | Lire le niveau qualité actuel | Faible |
| `unity_set_quality_level` | Changer le niveau qualité | Faible |
| `unity_get_physics_layers` | Lire la matrice de collision | Moyenne |
| `unity_set_physics_layer_collision` | Modifier collision entre layers | Moyenne |

### Catégories de Settings

```
SETTINGS CATEGORIES:
├── quality      → QualitySettings (shadows, AA, LOD, vsync...)
├── graphics     → GraphicsSettings (render pipeline, tiers...)
├── physics      → Physics (gravity, layers, solver...)
├── physics2d    → Physics2D (gravity 2D, simulation...)
├── time         → Time (timeScale, fixedDeltaTime...)
├── audio        → AudioSettings (sample rate, DSP buffer...)
├── render       → RenderSettings (fog, ambient, skybox...) [per-scene]
├── player       → PlayerSettings (company, product, resolution...)
└── lighting     → LightingSettings (GI, lightmapping...)
```

### Étapes d'Implémentation

#### Étape 1: Créer ProjectSettingsTools.cs (Complexité: Moyenne)
**Fichiers à créer:**
- `Assets/Editor/McpServer/Tools/ProjectSettingsTools.cs`

**Contenu:**
```csharp
// Structure de base
public partial class McpUnityServer
{
    static partial void RegisterProjectSettingsTools();

    // 6 outils à implémenter
}
```

#### Étape 2: Implémenter GetProjectSettings (Complexité: Moyenne)
**Catégories supportées:**
- quality, graphics, physics, physics2d, time, audio, render, player

**Paramètres:**
```json
{
  "category": "quality|graphics|physics|time|audio|render|player",
  "detailed": true  // optionnel, pour infos étendues
}
```

#### Étape 3: Implémenter SetProjectSettings (Complexité: Haute)
**Paramètres:**
```json
{
  "category": "quality",
  "settings": {
    "shadows": "All",
    "antiAliasing": 4,
    "vSyncCount": 1
  }
}
```

#### Étape 4: Outils spécialisés Quality Level (Complexité: Faible)
- `unity_get_quality_level` - Retourne index et nom
- `unity_set_quality_level` - Change le niveau

#### Étape 5: Outils Physics Layer Collision (Complexité: Moyenne)
- `unity_get_physics_layers` - Matrice de collision
- `unity_set_physics_layer_collision` - Modifier une paire

#### Étape 6: Ajouter au serveur TypeScript (Complexité: Faible)
- Déclarer les nouveaux outils dans `index.ts`
- Descriptions optimisées pour tokens

---

## 4. Spécifications Techniques

### Structure du Fichier ProjectSettingsTools.cs

```csharp
using System;
using System.Collections.Generic;
using McpUnity.Protocol;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace McpUnity.Server
{
    public partial class McpUnityServer
    {
        static partial void RegisterProjectSettingsTools()
        {
            // 6 outils à enregistrer
        }

        #region Settings Readers

        private static Dictionary<string, object> GetQualitySettings() { }
        private static Dictionary<string, object> GetGraphicsSettings() { }
        private static Dictionary<string, object> GetPhysicsSettings() { }
        private static Dictionary<string, object> GetTimeSettings() { }
        private static Dictionary<string, object> GetAudioSettings() { }
        private static Dictionary<string, object> GetRenderSettings() { }
        private static Dictionary<string, object> GetPlayerSettings() { }

        #endregion

        #region Settings Writers

        private static bool SetQualitySetting(string key, object value) { }
        private static bool SetPhysicsSetting(string key, object value) { }
        // etc.

        #endregion

        #region Tool Handlers

        private static McpToolResult GetProjectSettings(Dictionary<string, object> args) { }
        private static McpToolResult SetProjectSettings(Dictionary<string, object> args) { }
        private static McpToolResult GetQualityLevel(Dictionary<string, object> args) { }
        private static McpToolResult SetQualityLevel(Dictionary<string, object> args) { }
        private static McpToolResult GetPhysicsLayerCollision(Dictionary<string, object> args) { }
        private static McpToolResult SetPhysicsLayerCollision(Dictionary<string, object> args) { }

        #endregion
    }
}
```

### Propriétés par Catégorie

#### Quality Settings (lecture/écriture)
| Propriété | Type | Description |
|-----------|------|-------------|
| currentLevel | int | Index du niveau actuel |
| levelName | string | Nom du niveau |
| pixelLightCount | int | Nombre de pixel lights |
| shadows | enum | None/HardOnly/All |
| shadowResolution | enum | Low/Medium/High/VeryHigh |
| shadowDistance | float | Distance des ombres |
| antiAliasing | int | 0/2/4/8 |
| softParticles | bool | Particules soft |
| vSyncCount | int | 0/1/2 |
| lodBias | float | Multiplicateur LOD |
| maximumLODLevel | int | Niveau LOD max |

#### Physics Settings (lecture/écriture)
| Propriété | Type | Description |
|-----------|------|-------------|
| gravity | Vector3 | Gravité mondiale |
| defaultSolverIterations | int | Itérations solver |
| defaultSolverVelocityIterations | int | Itérations vélocité |
| bounceThreshold | float | Seuil de rebond |
| defaultContactOffset | float | Offset contact |
| autoSimulation | bool | Simulation auto |
| autoSyncTransforms | bool | Sync transforms auto |

#### Time Settings (lecture/écriture)
| Propriété | Type | Description |
|-----------|------|-------------|
| timeScale | float | Échelle de temps |
| fixedDeltaTime | float | Pas fixe (physics) |
| maximumDeltaTime | float | Delta max |
| maximumParticleDeltaTime | float | Delta max particules |

#### Render Settings (per-scene, lecture/écriture)
| Propriété | Type | Description |
|-----------|------|-------------|
| fog | bool | Brouillard activé |
| fogMode | enum | Linear/Exponential/ExponentialSquared |
| fogColor | Color | Couleur brouillard |
| fogDensity | float | Densité |
| fogStartDistance | float | Distance début |
| fogEndDistance | float | Distance fin |
| ambientMode | enum | Skybox/Trilight/Flat/Custom |
| ambientLight | Color | Lumière ambiante |
| ambientIntensity | float | Intensité ambiante |
| skybox | Material | Matériau skybox |

#### Audio Settings (lecture/écriture runtime)
| Propriété | Type | Description |
|-----------|------|-------------|
| sampleRate | int | Fréquence échantillonnage |
| speakerMode | enum | Mono/Stereo/Quad/Surround/Mode5point1/Mode7point1 |
| dspBufferSize | int | Taille buffer DSP |
| numRealVoices | int | Voix réelles |
| numVirtualVoices | int | Voix virtuelles |

#### Player Settings (Editor only, lecture/écriture)
| Propriété | Type | Description |
|-----------|------|-------------|
| companyName | string | Nom entreprise |
| productName | string | Nom produit |
| bundleVersion | string | Version |
| defaultScreenWidth | int | Largeur par défaut |
| defaultScreenHeight | int | Hauteur par défaut |
| fullScreenMode | enum | Mode plein écran |
| colorSpace | enum | Gamma/Linear |

---

## 5. Considérations Importantes

### Risques et Mitigations

| Risque | Impact | Mitigation |
|--------|--------|------------|
| Certaines propriétés Editor-only | Moyen | Vérifier `#if UNITY_EDITOR` |
| Changements destructifs | Haut | Undo.RecordObject obligatoire |
| RenderSettings per-scene | Moyen | Documenter clairement |
| AudioSettings reset tout | Haut | Avertir l'utilisateur |
| Valeurs invalides | Moyen | Validation stricte |

### Alternatives Considérées

1. **Un outil par setting** - Rejeté (trop de tokens, 50+ outils)
2. **Accès direct aux fichiers .asset** - Rejeté (fragile, non documenté)
3. **Approche générique retenue** - 6 outils couvrant tous les besoins

### Décisions Techniques

1. **Catégories vs settings individuels**: Catégories pour réduire le nombre d'outils
2. **Undo support**: Obligatoire pour toutes les modifications
3. **Validation**: Enum strings acceptés, conversion automatique
4. **Format retour**: JSON structuré avec métadonnées

---

## 6. Définitions des Outils pour index.ts

```typescript
// À ajouter dans defaultTools (avec defer_loading: true)

{
  name: "unity_get_project_settings",
  description: "SETTINGS: Get project settings by category (quality|graphics|physics|time|audio|render|player)",
  inputSchema: {
    type: "object",
    properties: {
      category: { type: "string", enum: ["quality", "graphics", "physics", "physics2d", "time", "audio", "render", "player"] },
      detailed: { type: "boolean", description: "Include extended info (default: false)" }
    },
    required: ["category"]
  }
},
{
  name: "unity_set_project_settings",
  description: "SETTINGS: Modify project settings. Pass category and settings object",
  inputSchema: {
    type: "object",
    properties: {
      category: { type: "string", enum: ["quality", "graphics", "physics", "physics2d", "time", "audio", "render", "player"] },
      settings: { type: "object", description: "Key-value pairs to set" }
    },
    required: ["category", "settings"]
  }
},
{
  name: "unity_get_quality_level",
  description: "SETTINGS: Get current quality level index and name",
  inputSchema: { type: "object", properties: {} }
},
{
  name: "unity_set_quality_level",
  description: "SETTINGS: Set quality level by index or name",
  inputSchema: {
    type: "object",
    properties: {
      level: { type: "integer", description: "Quality level index (0-based)" },
      levelName: { type: "string", description: "Or quality level name" },
      applyExpensiveChanges: { type: "boolean", description: "Apply AA changes (default: true)" }
    }
  }
},
{
  name: "unity_get_physics_layer_collision",
  description: "SETTINGS: Get physics layer collision matrix",
  inputSchema: {
    type: "object",
    properties: {
      layer1: { type: "string", description: "First layer name (optional, returns full matrix if omitted)" },
      layer2: { type: "string", description: "Second layer name (optional)" }
    }
  }
},
{
  name: "unity_set_physics_layer_collision",
  description: "SETTINGS: Enable/disable collision between two physics layers",
  inputSchema: {
    type: "object",
    properties: {
      layer1: { type: "string", description: "First layer name" },
      layer2: { type: "string", description: "Second layer name" },
      collide: { type: "boolean", description: "Enable collision between layers" }
    },
    required: ["layer1", "layer2", "collide"]
  }
}
```

---

## 7. Résumé du Plan

### Fichiers à Créer/Modifier

| Fichier | Action | Lignes estimées |
|---------|--------|-----------------|
| `Tools/ProjectSettingsTools.cs` | Créer | ~600 lignes |
| `McpUnityServer.cs` | Modifier | +2 lignes (partial declaration) |
| `Server~/src/index.ts` | Modifier | +60 lignes (tool definitions) |

### Timeline Estimé

1. **ProjectSettingsTools.cs** - Implémentation complète
2. **Déclarations index.ts** - Ajout des 6 outils
3. **Tests** - Vérification de chaque catégorie
4. **Documentation** - Mise à jour des instructions serveur

### Métriques de Succès

- [ ] 6 nouveaux outils fonctionnels
- [ ] Toutes les catégories de settings accessibles
- [ ] Undo support pour toutes les modifications
- [ ] Validation des entrées
- [ ] Tests passants sur chaque catégorie

---

## Sources

- [Unity QualitySettings API](https://docs.unity3d.com/ScriptReference/QualitySettings.html)
- [Unity PlayerSettings API](https://docs.unity3d.com/ScriptReference/PlayerSettings.html)
- [Unity Physics API](https://docs.unity3d.com/ScriptReference/Physics.html)
- [Unity RenderSettings API](https://docs.unity3d.com/ScriptReference/RenderSettings.html)
- [Unity Time API](https://docs.unity3d.com/ScriptReference/Time.html)
- [Unity AudioSettings API](https://docs.unity3d.com/ScriptReference/AudioSettings.html)
- [Unity Support: Modify Project Settings via scripting](https://support.unity.com/hc/en-us/articles/115000177803)
- [Unity GraphicsSettings API](https://docs.unity3d.com/ScriptReference/Rendering.GraphicsSettings.html)

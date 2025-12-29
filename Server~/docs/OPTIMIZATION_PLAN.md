# Plan d'Optimisation MCP Unity - Réduction Tokens

## 📊 Analyse Situation Actuelle

### Métriques Mesurées
| Élément | Taille | Tokens Estimés |
|---------|--------|----------------|
| Instructions serveur | 10,604 bytes | ~4,000 tokens |
| Définitions outils | 36,643 bytes | ~14,000 tokens |
| **Total index.ts** | **57,237 bytes** | **~22,000 tokens** |

### Structure Actuelle
- **52 outils total** (5 core + 47 defer_loading)
- **Instructions**: 195 lignes de documentation inline
- **Descriptions**: Redondantes et verboses

### Problèmes Identifiés
1. ❌ Instructions trop longues (~4000 tokens injectés à CHAQUE conversation)
2. ❌ Descriptions d'outils verboses avec exemples inline
3. ❌ Pas de cache côté serveur (Unity requêtée à chaque appel)
4. ❌ Réponses non compressées/paginées
5. ❌ Workflows documentés inline au lieu d'être accessibles via ressource

---

## 🎯 Plan d'Action Détaillé

### Phase 1: Réduction Instructions (Impact: -70% tokens instructions)

**Objectif**: Passer de ~4000 à ~1200 tokens

**Actions**:
```
1.1 Créer structure hiérarchique condensée
    - Remplacer le texte verbeux par des listes à puces courtes
    - Utiliser des abréviations standard (GO=GameObject, etc.)

1.2 Déplacer la documentation dans les ressources MCP
    - workflows://animator → Documentation complète Animator
    - workflows://materials → Documentation complète Materials
    - workflows://prefabs → Documentation complète Prefabs

1.3 Instructions minimales gardées:
    - Liste des 5 core tools
    - Catégories avec nombre d'outils
    - Règle outputMode='tree' pour économie tokens
    - Référence aux ressources pour détails
```

**Template Instructions Optimisé** (~1200 tokens):
```typescript
const serverInstructions = `Unity MCP (52 tools).

CORE (always loaded): unity_get_editor_state, unity_list_gameobjects, unity_get_component, unity_modify_component, unity_create_gameobject

CATEGORIES (use Tool Search):
- ANIMATOR: 16 tools (states, transitions, blend trees)
- ASSET: 5 tools (search, info, preview)
- SCENE: 4 tools | PREFAB: 4 tools | MATERIAL: 3 tools
- MEMORY: 3 tools | EDITOR: 8 tools

TOKEN RULES:
- outputMode='tree' for unity_list_gameobjects (90% smaller)
- returnBase64=false for screenshots
- size='small' for previews

RESOURCES: Read workflows://[category] for detailed docs.`;
```

### Phase 2: Optimisation Descriptions Outils (Impact: -40% tokens outils)

**Objectif**: Descriptions courtes mais précises

**Actions**:
```
2.1 Format standardisé pour chaque outil:
    "[CATEGORY]: [Action verb] [object]. Keywords: [3-5 mots]"

2.2 Supprimer exemples des descriptions
    - Les exemples vont dans les ressources MCP

2.3 Supprimer les "e.g." et phrases explicatives
```

**Exemple Avant/Après**:
```typescript
// AVANT (89 caractères):
description: "ANIMATOR: Get complete Animator Controller structure. Keywords: animation, state machine, layers, parameters"

// APRÈS (52 caractères):
description: "ANIMATOR: Read controller structure (states, layers, params)"
```

### Phase 3: Système de Cache Serveur (Impact: -50% appels Unity)

**Objectif**: Réduire les allers-retours avec Unity

**Actions**:
```
3.1 Cache en mémoire avec TTL
    const cache = new Map<string, {data: any, expiry: number}>();

3.2 Catégories de cache:
    - hierarchy: TTL 30s (change fréquemment)
    - assets: TTL 5min (change rarement)
    - editorState: TTL 5s (change souvent)
    - components: TTL 1min

3.3 Invalidation intelligente:
    - Après create/delete: invalider hierarchy
    - Après modify: invalider component spécifique
```

**Code Cache**:
```typescript
class ServerCache {
  private cache = new Map<string, CacheEntry>();

  get(key: string): any | null {
    const entry = this.cache.get(key);
    if (!entry || Date.now() > entry.expiry) return null;
    return entry.data;
  }

  set(key: string, data: any, ttlMs: number): void {
    this.cache.set(key, { data, expiry: Date.now() + ttlMs });
  }

  invalidate(pattern: string): void {
    for (const key of this.cache.keys()) {
      if (key.includes(pattern)) this.cache.delete(key);
    }
  }
}
```

### Phase 4: Réponses Compactes (Impact: -30% tokens réponse)

**Objectif**: Minimiser la taille des réponses

**Actions**:
```
4.1 Mode compact par défaut
    - unity_list_gameobjects: outputMode='tree' par défaut
    - Limiter maxDepth=3 par défaut

4.2 Pagination automatique
    - unity_search_assets: maxResults=20 par défaut
    - Ajouter "hasMore" flag si truncated

4.3 Champs optionnels
    - Ne retourner que les champs demandés
    - Exclure les métadonnées sauf si demandées
```

### Phase 5: Ressources MCP pour Documentation (Impact: -60% instructions)

**Objectif**: Déplacer la doc vers ressources consultables

**Actions**:
```
5.1 Créer ressources dynamiques:
    - workflows://core → Workflows de base
    - workflows://animator → Guide Animator complet
    - workflows://materials → Guide Materials complet
    - workflows://assets → Guide Asset Browser
    - examples://[tool-name] → Exemples d'utilisation

5.2 L'IA lit les ressources uniquement quand nécessaire
    - Premier contact: instructions minimales
    - Besoin Animator: lit workflows://animator
```

---

## 📈 Résultats Attendus

### Avant Optimisation
| Métrique | Valeur |
|----------|--------|
| Instructions | ~4,000 tokens |
| Tool defs | ~14,000 tokens |
| Réponse moyenne | ~500 tokens |
| Cache hits | 0% |

### Après Optimisation
| Métrique | Valeur | Réduction |
|----------|--------|-----------|
| Instructions | ~1,200 tokens | **-70%** |
| Tool defs | ~8,400 tokens | **-40%** |
| Réponse moyenne | ~350 tokens | **-30%** |
| Cache hits | ~50% | **+50%** |

### Impact Total Estimé
- **Tokens par conversation**: -55% en moyenne
- **Latence**: -40% (grâce au cache)
- **Coût API**: -50% estimé

---

## ⚠️ Considérations et Risques

### Risques
1. **Descriptions trop courtes** → L'IA peut mal choisir l'outil
   - Mitigation: Garder les keywords pertinents

2. **Cache stale** → Données obsolètes retournées
   - Mitigation: TTL courts + invalidation sur mutations

3. **Ressources non lues** → L'IA manque de contexte
   - Mitigation: Instructions indiquent quand lire les ressources

### Compatibilité
- ✅ Rétrocompatible (mêmes noms d'outils)
- ✅ Pas de changement côté Unity C#
- ✅ Seul index.ts modifié

---

## 🚀 Ordre d'Implémentation Recommandé

1. **Phase 1** (Instructions) - Impact immédiat, faible risque
2. **Phase 4** (Réponses compactes) - Quick win
3. **Phase 2** (Descriptions) - Optimisation progressive
4. **Phase 3** (Cache) - Nécessite tests
5. **Phase 5** (Ressources) - Refactoring plus important

---

## ✅ Métriques de Succès

- [ ] Instructions < 1,500 tokens
- [ ] Tool definitions < 10,000 tokens
- [ ] Cache hit rate > 40%
- [ ] Temps de réponse moyen < 500ms
- [ ] Aucune régression fonctionnelle

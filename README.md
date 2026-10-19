# Oxipital Packages

Les deux packages Unity du moteur Oxipital, à ajouter dans n'importe quel projet Unity 6 en HDRP :

| Package | Contenu |
|---|---|
| **Oxipital Core** (`Core/`) | Ballet d'orbes en VFX Graph, forces, mode vie infinie, flux optique, splats, formes d'émetteurs de base |
| **Oxipital Camera** (`Camera/`) | Modes orbital, vaisseau et capteur SOMI, transitions Cinemachine, sortie Spout / Syphon, suivi du dôme |

Les deux sont indépendants : on peut installer la caméra sans les particules, et inversement.
Le projet de développement (scènes, module Chataigne, TouchDesigner) est [Oxipital-Engine](https://github.com/Gamgie/Oxipital-Engine).

## Ajouter Oxipital à un projet

Prérequis : Unity 6000.3 avec HDRP, et [Git LFS](https://git-lfs.com) installé sur la machine (les modèles et textures sont stockés avec LFS).

1. Ouvrir `Packages/manifest.json` du projet et y ajouter les sources de paquets et les lignes Oxipital :

```json
{
  "scopedRegistries": [
    { "name": "OpenUPM", "url": "https://package.openupm.com", "scopes": ["com.benkuper", "com.pfc.dome-tools"] },
    { "name": "Keijiro", "url": "https://registry.npmjs.com", "scopes": ["jp.keijiro"] }
  ],
  "dependencies": {
    "com.gamgie.oxipital.core": "https://github.com/Gamgie/Oxipital-Packages.git?path=/Core#v1.0.0",
    "com.gamgie.oxipital.camera": "https://github.com/Gamgie/Oxipital-Packages.git?path=/Camera#v1.0.0",
    "org.nesnausk.gaussian-splatting": "https://github.com/aras-p/UnityGaussianSplatting.git?path=/package",
    "com.pfc.dome-tools": "0.8.1"
  }
}
```

   Les deux dernières lignes sont facultatives : la première pour les splats gaussiens, la seconde pour le dôme.
   Si le fichier a déjà ces sections, ajouter seulement les lignes manquantes.

2. Revenir dans Unity : les packages s'installent tout seuls. Les formes d'émetteurs de base sont copiées dans
   `Assets/StreamingAssets/emitters` au premier lancement (ou via le menu *Oxipital > Installer les émetteurs de base*).
3. Glisser les prefabs **Ballet** (Oxipital Core) et **Cameras** (Oxipital Camera) dans la scène.

## Mettre à jour un projet

Remplacer `#v1.0.0` par la nouvelle version dans les deux lignes du `manifest.json`. Un projet ne change jamais
de version tout seul : un spectacle déjà monté reste figé tant qu'on ne le décide pas.

## Modifier le moteur

Le moteur se développe dans [Oxipital-Engine](https://github.com/Gamgie/Oxipital-Engine), où ce dépôt est un
sous-module (`Oxipital-Packages/`). Voir le CLAUDE.md de chaque dépôt pour les règles et l'ordre des enregistrements.

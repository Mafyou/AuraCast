# AuraMusic

Partage le son d'un téléphone Android avec d'autres téléphones Android, en Bluetooth, sans Wi-Fi ni internet.
Une alternative maison à LE Audio Auracast pour les téléphones qui ne le supportent pas.

Le téléphone **master** joue sa musique (YouTube Music, Spotify, n'importe quelle app) et les téléphones qui **écoutent** entendent la même chose, avec un léger décalage (environ 200 ms de tampon plus la latence Bluetooth).

## Utilisation

1. **Une seule fois** : appairer les téléphones dans *Réglages › Bluetooth*.
2. Sur le master : **🎵 Diffuser mon son**, accepter les autorisations et la fenêtre « démarrer l'enregistrement », puis lancer la musique.
3. Sur l'autre téléphone : **🎧 Écouter**. Il retrouve le master tout seul parmi les téléphones appairés et se reconnecte automatiquement s'il le perd.

L'écran peut être éteint des deux côtés. La notification permet d'arrêter.

Hors de la maison, sans Wi-Fi commun : l'un active son partage de connexion et l'autre s'y connecte. Les deux téléphones sont alors sur le même réseau et le son passe en Wi-Fi, avec le Bluetooth en secours.

## Fonctionnement

```
 MASTER                                             ÉCOUTE
 App musicale ─► AudioPlaybackCapture               Téléphones appairés → connexion RFCOMM
                 PCM 48 kHz stéréo                        │
                      │                                   ▼
                 Encodeur Opus 96-160 kbps          Tampon d'environ 200 ms
                 (trames de 20 ms)                        │
                      │                                   ▼
                 Serveur RFCOMM  ───── Bluetooth ───►  Décodeur Opus ─► AudioTrack 🎧
                 (enregistrement SDP)                 (comble les pertes)
```

- **Capture** : `AudioPlaybackCapture` (Android 10+) via MediaProjection, dans un service au premier plan.
- **Transport** : Bluetooth Classic RFCOMM (trouvé via un enregistrement SDP, `AuraProtocol.ServiceUuid`) **et**, quand les téléphones sont sur le même Wi-Fi, TCP sur le réseau local, annoncé par une balise UDP chaque seconde sur chaque interface locale (`LanBeacon`). L'auditeur suit les deux et garde le premier exemplaire de chaque paquet numéroté (`SequenceGate`). Côté master, `LinkRouter` met le Bluetooth d'un téléphone en veille tant que son Wi-Fi suit, et le débit passe de 96 à 160 kbps quand tous les auditeurs sont en Wi-Fi. Le BLE L2CAP a été essayé en premier mais plafonnait à ~14 kbps.
- **Confiance** : un téléphone n'est accepté en Wi-Fi que s'il s'est déjà connecté en Bluetooth, donc appairé (`TrustedDevices`). Personne d'autre sur le réseau ne peut écouter.
- **Liens morts** : le master envoie un signal de vie chaque seconde quand il n'y a pas de son ; un lien muet 5 s est fermé et se reconnecte.
- **Protocole** (v3) : le master envoie un en-tête `AURA` + version + identifiant de session, l'auditeur répond par un « hello » (identifiant du téléphone + nom), puis viennent des paquets Opus préfixés par leur longueur et leur numéro, et des trames vides en signal de vie (`src/AuraMusic.Kernel/Protocol/AuraProtocol.cs`).
- **Lecture** : thread en priorité audio Android, sortie en virgule flottante. Un paquet en retard n'est comblé (dissimulation d'Opus) que si la sortie audio va manquer de son ; un retard accumulé est rattrapé en raccourcissant les trames de 1 ms avec un fondu, sans clic.
- **Dérive d'horloge** : les quartz des deux téléphones ne battent pas exactement à la même vitesse. `DriftController` surveille le son en attente et ajuste la vitesse de lecture d'au plus ±0,3 % (inaudible) pour le garder sur la cible, au lieu de laisser le tampon se vider ou déborder au fil des minutes.
- **Curseur de synchro** : pendant l'écoute, un curseur règle le délai total de 100 à 500 ms (200 par défaut) ; la cible n'est jamais plus basse que ce que la sortie audio du téléphone permet, plus deux paquets d'avance. En Bluetooth seul, elle ne descend pas sous 320 ms : la liaison arrive par rafales et se fige quelques centaines de millisecondes à chaque balayage Wi-Fi du téléphone. À gauche, le son colle à celui du master (même pièce) ; à droite, il encaisse mieux les à-coups radio. Le réglage s'applique en direct et est mémorisé (`PlayoutTuning`).
- **Diagnostic** : un double appui sur l'image de l'accueil ouvre un écran de chiffres en direct (codec, débit, trames par seconde de chaque lien, tampon, dérive, pertes), fourni par `DiagnosticsHub`.
- **Plusieurs auditeurs** : chaque connexion a sa propre file d'envoi. À 96 kbps, la liaison garde de la marge pour plusieurs téléphones (le Bluetooth classique accepte au plus 7 appareils connectés au master).
- **Spectre** : pendant la diffusion ou l'écoute, 16 bandes de fréquences façon Matrix (FFT maison dans le Kernel).
- **Mises à jour** : au lancement, l'app compare sa version à la dernière release GitHub et propose d'installer la nouvelle (même clé de signature : installation par-dessus, réglages conservés).

## Développement

- .NET 11 / C# 15 (unions, `field`, collections frozen), MAUI Android uniquement (API 29+).
- Codec Opus : **libopus natif** (binaires du paquet OpusSharp.Natives, appelés par un pont maison dans `Kernel/Codec/`), avec [Concentus](https://github.com/lostromb/concentus) (C# pur) en secours si la bibliothèque ne se charge pas. Les deux produisent le même flux.
- `src/AuraMusic.Kernel` : la logique sans dépendance Android, testable.
  - `State/` : l'état de l'app, une union `AuraState` publiée par `AuraHub` ;
  - `Protocol/` : le format des données échangées ;
  - `Playout/` : `PlayoutController`, qui décide toutes les 20 ms de jouer, rattraper, combler, sauter ou rebufferiser (une union `PlayoutStep`), `PcmCrossfade`, `DriftController` (dérive d'horloge) et `PlayoutTuning` (délai choisi au curseur) ;
  - `Diagnostics/` : les relevés du master et de l'écoute (une union `DiagnosticsReport`) et leur mise en texte ;
  - `Spectrum/` : l'analyseur de spectre ;
  - `Updates/` : lecture de la dernière release GitHub et comparaison des versions (une union `UpdateCheck`) ;
  - `Codec/` : encodeur et décodeur Opus (`OpusCodec`, natif ou C#) ;
  - `Localization/` : choix de la langue (FR par défaut, EN) ;
  - `Multipath/` : fusion des liens Bluetooth et Wi-Fi (`SequenceGate`), répartition côté master (`LinkRouter`), balise réseau (`LanBeacon`) et téléphones de confiance (`TrustedDevices`).
- `src/AuraMusic.Mobile` : l'app. Code Android dans `Platforms/Android/Casting/` (`BroadcastService` côté master, `ListenService` côté écoute).
- `tests/AuraMusic.Kernel.Tests` : tests unitaires xUnit v3 + Shouldly + Moq.
- Chaque projet regroupe ses `global using` dans un `Usings.cs` à sa racine.
- Apparence : toutes les couleurs sont dans `Resources/Styles/Colors.xaml`, tous les styles de composants dans `Resources/Styles/Styles.xaml` ; le code C# les lit par `Theme.Color` / `Theme.Style`.

```sh
dotnet test --project tests/AuraMusic.Kernel.Tests
```

### Déployer sur deux téléphones

`dotnet build -t:Run` n'installe pas sur le deuxième téléphone : il croit l'APK déjà installé. Il vaut mieux installer explicitement :

```sh
dotnet build src/AuraMusic.Mobile -f net11.0-android
adb install -r src/AuraMusic.Mobile/bin/Debug/net11.0-android/fr.mafyou.auramusic-Signed.apk
```

### Mesures

```sh
adb logcat -s AuraMusic
```

- côté master : `tx … frames/s, … frames/write, … ms/write, queued, dropped` ;
- côté écoute : `rx … frames/s, … kbps, concealed, skipped, rebuffers, dropped`.

50 trames/s veut dire que la liaison suit le temps réel.

### Intégration continue

`main` est protégée : tout passe par une pull request. Sur chacune, [le workflow de vérification](.github/workflows/pr.yml) lance les tests du Kernel et compile l'app Android en Release, **warnings traités comme des erreurs** ; les deux doivent être au vert pour fusionner. Il vérifie aussi que la version du csproj a été montée : une pull request qui la laisse sur un numéro déjà publié est refusée.

### Release

Le numéro de version vit dans `src/AuraMusic.Mobile/AuraMusic.Mobile.csproj` (`ApplicationDisplayVersion`). Pour publier :

1. chaque pull request monte ce numéro (c'est vérifié), il n'y a donc rien de plus à faire qu'à la fusionner ;
2. lancer à la main [le workflow de release](.github/workflows/ci.yml) sur `main` : **Actions › Release AuraMusic APK › Run workflow**, ou :

```sh
gh workflow run ci.yml --ref main
```

Le workflow lit la version dans le csproj, lance les tests du Kernel, compile l'APK Release signé (**warnings traités comme des erreurs**), crée le tag `v` + version sur le commit compilé et la release GitHub avec l'APK attaché et son empreinte SHA-256. Il refuse une autre branche que `main` et une version déjà publiée (il faut alors monter le numéro). Le SDK .NET est épinglé dans `global.json`.
Le workflow a besoin de deux secrets du dépôt : `ANDROID_KEYSTORE_BASE64` (le keystore, alias `mafyou`, encodé en base64) et `ANDROID_KEYSTORE_PASSWORD`. Toutes les versions doivent être signées avec la même clé, sinon Android refuse d'installer la mise à jour.

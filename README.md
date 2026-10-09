# AuraCast

Partage le son d'un téléphone Android avec d'autres téléphones Android, en Bluetooth, sans Wi-Fi ni internet.
Une alternative maison à LE Audio Auracast pour les téléphones qui ne le supportent pas.

Le téléphone **master** joue sa musique (YouTube Music, Spotify, n'importe quelle app) et les téléphones qui **écoutent** entendent la même chose, avec un léger décalage (un tampon de 100 ms plus la latence Bluetooth).

## Utilisation

1. **Une seule fois** : appairer les téléphones dans *Réglages › Bluetooth*.
2. Sur le master : **🎵 Diffuser mon son**, accepter les autorisations et la fenêtre « démarrer l'enregistrement », puis lancer la musique.
3. Sur l'autre téléphone : **🎧 Écouter**. Il retrouve le master tout seul parmi les téléphones appairés et se reconnecte automatiquement s'il le perd.

L'écran peut être éteint des deux côtés. La notification permet d'arrêter.

## Fonctionnement

```
 MASTER                                             ÉCOUTE
 App musicale ─► AudioPlaybackCapture               Téléphones appairés → connexion RFCOMM
                 PCM 48 kHz stéréo                        │
                      │                                   ▼
                 Encodeur Opus 128 kbps             Tampon de 100 ms
                 (trames de 20 ms)                        │
                      │                                   ▼
                 Serveur RFCOMM  ───── Bluetooth ───►  Décodeur Opus ─► AudioTrack 🎧
                 (enregistrement SDP)                 (comble les pertes)
```

- **Capture** : `AudioPlaybackCapture` (Android 10+) via MediaProjection, dans un service au premier plan.
- **Transport** : Bluetooth Classic RFCOMM sans chiffrement applicatif, trouvé via un enregistrement SDP (`AuraProtocol.ServiceUuid`). Le BLE L2CAP a été essayé en premier mais plafonnait à ~14 kbps sur nos téléphones.
- **Protocole** : un en-tête `AURA` + version, puis des paquets Opus préfixés par leur longueur (`Core/AuraProtocol.cs`).
- **Lecture** : le décodage suit l'horloge de lecture. Un paquet en retard est comblé par la dissimulation de pertes d'Opus et le retard accumulé est rattrapé en sautant une trame.
- **Plusieurs auditeurs** : chaque connexion a sa propre file d'envoi. En pratique, 3 ou 4 téléphones à 128 kbps (le Bluetooth classique accepte au plus 7 appareils connectés au master).

## Développement

- .NET 11 / MAUI, Android uniquement (API 29+), codec Opus [Concentus](https://github.com/lostromb/concentus).
- Code Android : `src/AuraCast.Mobile/Platforms/Android/Casting/` (`BroadcastService` côté master, `ListenService` côté écoute).
- État de l'app : `Core/AuraState.cs`, une union C# 15 publiée par `AuraHub`.

### Déployer sur deux téléphones

`dotnet build -t:Run` n'installe pas sur le deuxième téléphone : il croit l'APK déjà installé. Il vaut mieux installer explicitement :

```sh
dotnet build src/AuraCast.Mobile -f net11.0-android
adb install -r src/AuraCast.Mobile/bin/Debug/net11.0-android/com.mafyou.auracast-Signed.apk
```

### Mesures

```sh
adb logcat -s AuraCast
```

- côté master : `tx … frames/s, … frames/write, … ms/write, queued, dropped` ;
- côté écoute : `rx … frames/s, … kbps, concealed, skipped, rebuffers, dropped`.

50 trames/s veut dire que la liaison suit le temps réel.

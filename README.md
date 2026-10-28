# Escondite — Ver.Saüc Pellejero

## Grup
- Nom del grup: **Escondite**
- Membres:
  - Saüc Pellejero

---

## Versió de Unity
Projecte fet amb Unity 6000.5.5f1.

---

## Escena principal
L’escena inicial del projecte és: HS_Menu.

---

## Com executar-ho

### Fitxer executable
L’executable principal és: Escondite.exe.

### Qui fa de host i qui de client
1. Un jugador obre el joc i tria Host (Server).
2. Introdueix:
   - nom de sala
   - port (per defecte 9050)
   - màxim de jugadors (fins a 8)
3. Els altres jugadors obren el joc i trien Guest (Client).
4. Han d’introduir:
   - **IP del host** (LAN real, no només 127.0.0.1)
   - port (el mateix que el host)

> Important: per jugar en dos PCs diferents, el host ha de compartir la seva IP local (`ipconfig`) i permetre el firewall si Windows ho demana.

### Flux de joc
- **HS_Menu** → crear o unir-se
- **HS_WaitingRoom**:
  - xat
  - llista de jugadors
  - kick (només host)
  - start (només host, només es pot començar si hi ha 2 jugadors com a mínim)
- **HS_Game**:
  - FPS simple: moure’s, córrer, saltar, disparar
  - quan queda 1 viu, acaba ronda
  - host pot fer Play Again o Back to Lobby

---

## Xarxa: enviem estat o input? I per què?

Enviem principalment input del client cap al servidor (moviment, càmera, salt, córrer, disparar), no l’estat final del player.

### Per què?
- El servidor és autoritatiu: decideix posicions vàlides, impactes, morts i guanyador.
- Això redueix inconsistències.
- UDP és millor per missatges freqüents i petits.
- El servidor envia snapshots d’estat a tots els clients perquè es sincronitzin.

Resum:
- Client → Server: input
- Server → Clients: estat autoritatiu (snapshot)

---

## Protocol i dades

- **Lobby**: missatges de text + missatge de PlayerInfo (binari amb type byte).
- **Gameplay**: tot en UDP binari amb `GameMsgType`:
  - `Hello`
  - `Welcome`
  - `Input`
  - `Snapshot`

---

## Extres fets

- Moviment complet de jugador en 3D
- Córrer mantenint Shift
- Salt amb Space
- Disparar amb Left Mouse Click
- Morts per impacte
- Condició de victòria
- Reinici de ronda
- Tornar al lobby des del joc
- Fins a 8 jugadors al lobby

---

## Bugs coneguts

1. **Predicció local simple**  
   El jugador local pot notar petites correccions de posició (snap) si hi ha desync o pèrdua de paquets.

2. **Projectil visual**  
   Els projectils visibles dels dispars no son realment el dispar, ja que aquest ocorre de forma immediata, estil hitscan.

3. **Dependència de firewall/ports**  
   Si no s’obren els ports UDP (lobby i joc), els clients no reben snapshots i fan timeout.

---

## Què ha canviat des de l’entrega 2

- Hem passat d’una base de sockets/lobby a una arquitectura completa **lobby + partida**.
- S’ha separat clarament:
  - canal de lobby
  - canal de gameplay UDP binari
- S’ha implementat:
  - handshake de joc
  - input replication
  - snapshots autoritatius
  - sistema de mort i victòria
  - reinici de ronda
- S’ha mantingut el model correcte de fils:
  - fil de xarxa només rep i encola
  - `Update()` processa i toca Unity

---


## Controls del joc

- **W / A / S / D**: moure’s
- **Mouse**: mirar
- **Shift**: córrer
- **Space**: saltar
- **Left Click**: disparar
- **ESC**: moure/desapareixer cursor

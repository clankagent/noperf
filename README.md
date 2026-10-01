# NOPerf

NOPerf is a small set of optional add-ons (plugins) for the **Nuclear Option
Linux dedicated server**. They make a few small, often-repeated jobs inside the
server cheaper. The goal is to leave gameplay unchanged. Our automatic tests
found the same units and routes in the cases they checked.

**[See what we learned, in pictures](https://clankagent.github.io/noperf/)** ·
**[Watch how it works (step-by-step animation)](https://clankagent.github.io/noperf/how-it-works.html)** ·
**[Download v0.1.0](https://github.com/clankagent/noperf/releases/tag/v0.1.0)**

## The honest result

Some tiny jobs got cheaper in our tests. **We have not shown that the server as
a whole runs faster.** Those jobs were a very small part of the server's work,
and our long side-by-side test was inconclusive because the two battles played
out differently.

**Real players joining a server with NOPerf has not been tested yet.** Players
do not need to install anything, but treat player connections as untested.

## The four plugins

| Plugin | What it does | Starts |
|---|---|---|
| Spatial | Finds nearby units with less bookkeeping. Same units, same order. | On |
| Wrecks | Reuses temporary lists of wrecks instead of making new ones. | Off (experimental) |
| Navigation | Checks road points faster while planning routes. Same routes. | Off (experimental) |
| Diagnostics | Optional measurement. Writes timing numbers to the log. Adds a little work. | Off |

Install only the ones you want. Each works on its own.

NOPerf only works on the exact game version we tested: Nuclear Option 0.34.1
(Steam build 24724541) with BepInEx 5. On any other version the plugins switch
themselves off and the game runs normally.

## Install

1. Install [BepInEx 5](https://docs.bepinex.dev/) for Linux in the server
   folder. Start the server once, then stop it.
2. Open `BepInEx/config/BepInEx.cfg` and set:

   ```ini
   [Chainloader]
   HideManagerGameObject = true
   ```

   Without this setting the plugins do not run correctly.
3. Download the plugin archive from the
   [v0.1.0 release](https://github.com/clankagent/noperf/releases/tag/v0.1.0)
   and copy the plugin files you want into `BepInEx/plugins/`.
4. Start the server. Each plugin creates its own settings file in
   `BepInEx/config/`. To turn a plugin on or off, change `Enabled` in that
   file and restart the server.

To remove NOPerf, stop the server and delete the NOPerf files from
`BepInEx/plugins/`.

Full instructions, every setting, build steps and the detailed results are in
the **[developer guide](docs/developer-guide.md)**.

## How it was tested

The tests ran inside the real game server, comparing the original game code
with the modified code on the same inputs. We also ran real missions for hours.
[What is tested, and what is not yet](docs/testing.md).

## Contributing and license

Issues and pull requests are welcome. Please read
[CONTRIBUTING.md](CONTRIBUTING.md) first.

NOPerf's code, documentation and tests are released under the
[MIT License](LICENSE). Nuclear Option, BepInEx and other dependencies belong to
their owners and keep their own licenses. This project is unofficial and not
affiliated with the game's developers.

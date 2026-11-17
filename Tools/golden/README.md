# Golden fixtures

`make-golden.mjs` runs the original web build's pure game logic (`safe-route/dist/game/state.js`, `pack.js`, `items.js`
from github.com/Sunny8197783/AiOn) and writes `Assets/_AiOn/Tests/EditMode/Fixtures/golden-state.json`.
The EditMode `ParityTests` compare the C# port against it.

To regenerate: copy those three files into `Tools/golden/game/`, add `{"type":"module"}` as `Tools/golden/package.json`, then

    node Tools/golden/make-golden.mjs Assets/_AiOn/Tests/EditMode/Fixtures/golden-state.json

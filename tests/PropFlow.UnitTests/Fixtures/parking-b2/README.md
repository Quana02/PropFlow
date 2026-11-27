# B2/B3 regression fixture

The user supplied the diagonal parking drawing and reported a regression on
25/11/2026. These snapshots were read from the two saved drafts in a read-only
transaction, without changing either draft or published data.

- `source.jpg`: same source for both drafts (SHA-256
  `5CF243BB4F09B13A3A6F5F85DD6904B06657DD8D4DB2CD07A50456722AAB4643`).
- `b2.json`: good reference, 8 SPACE, 2 WALL, 1 LANE; no overlapping bays.
- `b3.json`: regressed output, 8 SPACE, 4 LINE; overlapping bays.

Recognition tests convert these saved shapes back into provider coordinates
and exercise the actual reader using a fake HTTP transport. They verify
geometry preservation, tool types, review fallback and overlap rejection.
They do not prove that a new live Gemini response will reproduce B2.

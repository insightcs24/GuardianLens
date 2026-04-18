#GuardianLens

# Brief Overview — GuardianLens

# The Problem it solves:
Sports piracy costs the industry $28 billion annually. The core failure is that existing tools are reactive and brittle — rights teams discover violations days after they happen, and any re-encoding or cropping breaks fingerprint detection. There is also no trustworthy timestamped record of who owned what, when.

# What GuardianLens does:
When a sports organisation uploads a video or image, GuardianLens instantly computes a 64-bit perceptual hash (pHash) using a Discrete Cosine Transform algorithm — a mathematical fingerprint that survives JPEG re-compression, resizing, brightness changes, and minor cropping. It also embeds an invisible watermark token into the pixel layer using LSB steganography, so ownership can be proven even from a cropped screenshot. As a Web3 twist, a SHA-256 commitment of the pHash is minted to the Polygon blockchain — creating a public, immutable, timestamped record that no one can alter retroactively.

A Python Scrapy crawler then continuously sweeps YouTube, Twitter, Instagram, Telegram, and Reddit every 15 minutes, downloading found images and comparing their pHash against the index. When a match exceeds 90% similarity, the Alert Engine automatically classifies it — anything above 97% confidence fires a DMCA takedown bot that sends platform-specific removal requests (YouTube Content ID API, Twitter API, email for Telegram) without any human involvement.

The entire system is controlled through a React 18 dashboard hosted on a Windows EC2 instance behind a YARP C# reverse proxy — so the frontend, API, and file serving all run from one URL with no external hosting dependencies.
Why judges will care: Most hackathon submissions talk about the problem. GuardianLens has a working prototype with a live React dashboard, real pHash computation that passes tests, blockchain transactions you can verify on Polygonscan, and a two-command deployment to production. Four BCA second-year students built the full stack from scratch — C# DCT algorithm, Solidity smart contract, YARP proxy, Python crawler, and React UI. 

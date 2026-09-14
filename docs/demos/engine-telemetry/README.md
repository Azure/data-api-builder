# Engine telemetry: manual Clipchamp presentation pack

Twelve numbered **1920 x 1080, 16:9 PNG slides**, each with one numbered **plain-text TTS script**. The script suffix matches the slide suffix exactly. This is a design walkthrough, not a demonstration of an implemented telemetry feature.

**Estimated duration: 8 minutes 40 seconds** at 145 spoken words per minute, including two seconds of breathing room per slide. The scripts contain **1,186 whitespace-delimited words**. At 135 words per minute, the same rounded timing model is approximately 9 minutes 16 seconds. These are planning estimates, not measured Clipchamp audio durations; verify the final timeline stays below ten minutes after generating narration.

- [slide-overview.png](slide-overview.png): visual contact sheet, for review only.
- [timing.csv](timing.csv): numbered manifest and estimated timing in seconds.
- [../../design/engine-telemetry.md](../../design/engine-telemetry.md): complete property catalog, design tradeoffs, and sources.

No TTS audio, video, engine implementation, cloud telemetry experiment, or Azure resource has been created by this pack. No content needs to be uploaded by the renderer.

## Assemble manually in Clipchamp

1. Create a **16:9** project. Import the twelve PNGs from the slides subfolder; do not import the overview as a thirteenth slide.
2. Put the images on the timeline in numeric order, from 01 through 12. Use full-frame fit, without cropping or zooming.
3. For each image, open the matching script below and copy **all of its text** into Clipchamp's text-to-speech editor. The files contain narration only: no titles, timestamps, or stage directions to remove.
4. Use the same voice, language, and pacing throughout. Preview the product names. The scripts use "Dab," "Data X," "dot net," "K Q L," and "H T T P" where helpful for pronunciation.
5. Place each generated narration clip at the beginning of its matching image. Set the image duration to the **actual audio length plus roughly two seconds**, then begin the next slide and narration together. The table is a planning guide; the generated audio is authoritative.
6. Prefer hard cuts and no background music for this technical review. Avoid transitions that obscure text or complicate the one-to-one alignment.
7. Watch the complete timeline. Check numbers, pronunciation, audio-to-slide alignment, and total duration. If it approaches ten minutes, reduce pauses or adjust speech pace before export; do not cut off narration.
8. Export at **1080p**. Use approved work-account sharing and the appropriate sensitivity settings for internal design material.

## One-to-one mapping and timing

Times below use each script's word count divided by 145 words/minute, plus two seconds, rounded up to the next whole second. They are not recorded audio lengths.

| Slide image | Matching TTS script | Topic | Words | Target duration | Estimated timeline |
| --- | --- | --- | --- | --- | --- |
| [slides/slide01.png](slides/slide01.png) | [scripts/text-to-speech-script01.txt](scripts/text-to-speech-script01.txt) | Goal and design status | 91 | 0:40 | 0:00-0:40 |
| [slides/slide02.png](slides/slide02.png) | [scripts/text-to-speech-script02.txt](scripts/text-to-speech-script02.txt) | Application Insights foundation | 96 | 0:42 | 0:40-1:22 |
| [slides/slide03.png](slides/slide03.png) | [scripts/text-to-speech-script03.txt](scripts/text-to-speech-script03.txt) | Three separate telemetry streams | 99 | 0:43 | 1:22-2:05 |
| [slides/slide04.png](slides/slide04.png) | [scripts/text-to-speech-script04.txt](scripts/text-to-speech-script04.txt) | Property choices and tradeoffs | 103 | 0:45 | 2:05-2:50 |
| [slides/slide05.png](slides/slide05.png) | [scripts/text-to-speech-script05.txt](scripts/text-to-speech-script05.txt) | Naming and counting semantics | 100 | 0:44 | 2:50-3:34 |
| [slides/slide06.png](slides/slide06.png) | [scripts/text-to-speech-script06.txt](scripts/text-to-speech-script06.txt) | The two data paths | 103 | 0:45 | 3:34-4:19 |
| [slides/slide07.png](slides/slide07.png) | [scripts/text-to-speech-script07.txt](scripts/text-to-speech-script07.txt) | DAB-managed Azure Monitor | 97 | 0:43 | 4:19-5:02 |
| [slides/slide08.png](slides/slide08.png) | [scripts/text-to-speech-script08.txt](scripts/text-to-speech-script08.txt) | DataX value and onboarding | 100 | 0:44 | 5:02-5:46 |
| [slides/slide09.png](slides/slide09.png) | [scripts/text-to-speech-script09.txt](scripts/text-to-speech-script09.txt) | DataX compatibility questions | 99 | 0:43 | 5:46-6:29 |
| [slides/slide10.png](slides/slide10.png) | [scripts/text-to-speech-script10.txt](scripts/text-to-speech-script10.txt) | Cost and volume | 98 | 0:43 | 6:29-7:12 |
| [slides/slide11.png](slides/slide11.png) | [scripts/text-to-speech-script11.txt](scripts/text-to-speech-script11.txt) | Optional two-stage adoption | 100 | 0:44 | 7:12-7:56 |
| [slides/slide12.png](slides/slide12.png) | [scripts/text-to-speech-script12.txt](scripts/text-to-speech-script12.txt) | Review decisions and next steps | 100 | 0:44 | 7:56-8:40 |
| **Total** | **12 matching pairs** | | **1,186** | **8:40** | |

## Editing the pack

- Edit each narration file directly. The wording intentionally expands technical acronyms for speech; the slides retain the canonical identifiers.
- Edit [source/slide-content.json](source/slide-content.json) to change slide text. Its source-section list for each slide identifies the corresponding discussion in the full design document.
- [source/RenderSlides.cs](source/RenderSlides.cs) is a local, standalone .NET Framework renderer. It uses Windows System.Drawing, Segoe UI, and Consolas, with System.Web.Extensions for reading the JSON. It is not part of the DAB solution and requires no extra NuGet packages.
- To regenerate, compile the renderer as a console executable with references to System.Drawing and System.Web.Extensions, then pass this demo directory as its only argument. It replaces the generated slide PNGs and overview and reports text overflow rather than silently shrinking the type. It does not change script execution policy, generate audio, or call a network service.
- Keep the numeric suffixes aligned. If narration changes, recalculate word counts and timing estimates in this guide and the manifest. Validate the actual TTS timing again in Clipchamp.

## Evidence and review boundaries

The presentation summarizes the design document dated **2026-09-09**. It is intentionally selective; use the full catalog to review individual properties rather than treating the slide examples as the complete approved payload.

Application Insights is the common design direction. Exact client/exporter selection, field approval, consent, external-OSS publishing, and platform adoption remain undecided. Remaining in Azure Monitor is a valid option; DataX is not a committed second stage.

DataX specifics reflect the onboarding-guide copy supplied for the design discussion, not an independently verified current service commitment or DAB approval. The approximate onboarding estimate, device-oriented identity conventions, eligibility rule, US processing, freshness, retention, and costs require owner confirmation. Review internal source material and sharing permissions before distributing the presentation outside the intended audience.

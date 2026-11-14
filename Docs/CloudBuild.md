# Android APK builds from GitHub

The workflow `.github/workflows/android-build.yml` builds an installable development APK and
offers it for download from GitHub. It is started by hand and can be started from a tablet.

**Status (14 November 2026):** Route 2, the self-hosted runner on the owner's laptop
(`LAPTOP-SG8SSFTD`), has built APKs; Route 1 has never run, because no licence secrets are set.
Every run is listed in `Docs/LivingMagicTesting.md` with its commit and result. Since LM-08 the
self-hosted job can also run the EditMode tests before building (input **editmode_tests**, on by
default).

## The decision you need to make first: licensing

A build needs the Unity Editor to run with a valid licence. There are two places it can run,
and they differ in what the licence clearly allows.

### What was verified, and where

| Finding | Source |
| --- | --- |
| GameCI publishes an Android build image for Unity 6000.6.0f1 (`unityci/editor:ubuntu-6000.6.0f1-android-3`, about 8 GB) | Docker Hub, checked 13 November 2026 |
| GameCI's documented method for a Personal licence: activate in Unity Hub on any computer, then store the contents of the licence file and the account's email and password as secrets | GameCI documentation, version 4 |
| Unity's own manual states that manual activation "doesn't support ... Unity Personal", and that Personal is activated by logging in to the Unity Hub | Unity Manual, manual activation guide |
| Unity Personal is only for those with under USD 200,000 of revenue and funding in the last 12 months; above that it may not be used "at all, even for internal projects or prototyping" | Unity Editor Software Terms, section 1.1 |
| "You may not use Unity Build Server with the Unity Personal tier" | Unity Editor Software Terms, section 2.5 |
| The right to install Unity on build machines is granted for Unity Pro (up to ten internal build machines) | Unity Editor Software Terms, section 2.4 |
| One seat may run only one instance of the Editor at a time, and "in no event may third party service providers directly or indirectly distribute, run or use the Unity Editor" | Unity Editor Software Terms, section 2.8 |

### What this means

- **Technically**, a cloud build with a Personal licence is expected to work through GameCI's
  method. It is a community method, not one Unity documents. GameCI's tooling also notes that a
  Personal licence file stops working after some time and must then be replaced.
- **Contractually, it is not clear that it is allowed.** Unity's terms grant build-machine use
  for Pro, exclude Personal from Unity Build Server, and restrict third parties running the
  Editor. They do not say in plain words whether a Personal licence may be used on a hosted
  build machine. I could not confirm that it is permitted, and this is not legal advice.
- **Eligibility comes first.** The USD 200,000 threshold applies to the organisation that owns
  the project. If NGG is above it, Unity Personal cannot be used for Threefold at all, on any
  machine, and Unity Pro is required (listed by Unity at about USD 2,310 per seat per year;
  verify the current price).

Because of this, the workflow offers two routes. **Route 2 is the one I recommend until the
licensing question is settled**, because it runs Unity on your own computer under the licence
that is already activated there.

| | Route 1: GitHub-hosted | Route 2: self-hosted on your Windows PC |
| --- | --- | --- |
| Where Unity runs | A GitHub Linux runner, in a GameCI container | Your PC, using the Unity already installed and activated |
| Laptop needed | Once, to copy the licence file | It must be switched on and online when a build is started |
| Secrets stored in GitHub | Licence file, Unity email and password | None |
| Cost | Uses the organisation's Actions allowance | No Actions minutes are charged for self-hosted runners |
| Licence position | Unclear for Unity Personal, as described above | The same use of Unity as building by hand on that PC |
| Started from the tablet | Yes | Yes |

## Route 1: GitHub-hosted

### Secrets

Create them under **Settings > Secrets and variables > Actions > Secrets**.

| Secret | Personal licence | Pro licence |
| --- | --- | --- |
| `UNITY_LICENSE` | The full contents of `C:\ProgramData\Unity\Unity_lic.ulf` | Not used |
| `UNITY_SERIAL` | Not used | The serial key from the Unity subscription page |
| `UNITY_EMAIL` | The Unity account's email | The same |
| `UNITY_PASSWORD` | The Unity account's password | The same |

Notes:

- `C:\ProgramData` is a hidden folder. If the file is missing, open Unity Hub and add the
  licence again under **Preferences > Licenses**.
- GitHub encrypts secrets, never shows them again, and masks them in logs. The workflow only
  tests whether they exist; it never prints them. Do not put them in any file in the repository.
- Storing an account password in a third-party service is a risk in itself. Consider a Unity
  account used only for builds, if your licence allows it.
- If a build stops at activation after working earlier, the licence file has probably expired.
  Copy it again from the laptop.

### Cost

The repository is private, so runs are billed against the organisation's Actions allowance.
According to GitHub's billing documentation, GitHub Free for organisations includes USD 12 a
month and a standard Linux runner costs USD 0.006 a minute, which is about 2,000 minutes.

How long an Android build takes is not known yet. The first run downloads an 8 GB image,
imports the whole project and compiles it with IL2CPP; later runs reuse a cache. The job is
stopped after 120 minutes at the latest, which caps one run at about USD 0.72. Read the real
duration from the first run before building often. Nothing in this workflow configures billing
or uses larger runners.

## Route 2: self-hosted runner on the Windows PC

One-time setup on the PC, about ten minutes:

1. On GitHub open **Settings > Actions > Runners > New self-hosted runner** and choose Windows.
2. Run the commands GitHub shows there, in PowerShell. When the configuration asks for
   **additional labels**, enter `threefold-unity`. When it asks whether to run as a service,
   answer yes, so the runner starts with the PC.
3. Only if Unity is not at `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`:
   create the repository variable `UNITY_EDITOR_PATH` with the full path to `Unity.exe`.

Requirements on the PC: Unity 6000.6.0f1 with the Android Build Support module (including the
SDK, NDK and OpenJDK), activated in Unity Hub; Git with Git LFS.

Things to know:

- The runner keeps its own copy of the repository in its work folder. It does not touch
  `C:\dev\Threefold\threefold` or any unsaved work there.
- Close the Unity Editor before starting a build. One licence seat may run one Editor at a time.
- A self-hosted runner executes whatever the repository's workflows tell it to. That is safe
  for a private repository whose contributors you trust. Do not attach it to a public repository.
- If the PC is off, a started build waits in the queue until the PC is on, or until it is cancelled.

## Starting a build from the tablet

1. Open the repository on GitHub and go to **Actions**.
2. Choose **Android build** in the list on the left.
3. Tap **Run workflow**, pick **self-hosted** (or **github-hosted** once licence secrets exist),
   leave **Run the EditMode tests** ticked, and tap the green button. Untick **Build the APK** to
   run only the tests (about 15 minutes instead of about an hour).
4. Open the run when it appears. When it has finished, scroll to **Artifacts**.
5. Download `threefold-android-development-<number>`. It is a zip containing `Threefold.apk`.
   The test results are in `threefold-editmode-tests-<number>` (`editmode-results.xml`, NUnit format).
   The run's page shows the commit it was built from.
6. Unzip it, open the APK and allow installation from that source when Android asks.

Starting a second build cancels one that is still running, so only one build uses a runner at a time.

Artifacts are kept for 14 days. The APK is a development build signed with a debug key: it
installs on a device, and it cannot be published to a store.

## When a build fails

Download the artifact `threefold-android-logs-<number>` and give it to Claude Code.

| File | Contains | Present when |
| --- | --- | --- |
| `build-report.txt` | Result, size, duration, each build step, every error and warning | Unity reached the build |
| `threefold-build.log` | Everything Unity logged during scene assembly and the build | The build method started |
| `android-build.log` | Unity's complete Editor log | Route 2 only |
| `editmode-results.xml`, `editmode-tests.log` (test artifact) | Each EditMode test with its result and message; Unity's log of the test run | Route 2, when the tests were run |

A failing EditMode test does not stop the APK from being built, but the run is marked failed and
the step "Run EditMode tests" lists the failed tests.

**"The project is open in Unity: Temp\UnityLockfile is in use."** A Unity process still has the
runner's copy of the project open. This happens when a build was cut off (the laptop shut down or
restarted, or the runner was stopped) and its Unity kept running. Close your own Unity Editor (saving
your work), then in Task Manager, Details tab, end any `Unity.exe` that is left, and start the build
again. A lock file left behind by an interrupted run with no Unity running is set aside by the scripts
automatically (renamed to `UnityLockfile.stale-...`, not deleted).

If Unity stops before the build method starts, for example because activation failed or a
script does not compile, the first two files do not exist. On Route 1 the cause is then only in
the output of the "Build development APK" step, which must be copied from the run's page. This
is a limit of the GameCI action, which writes Unity's own log to the console and not to a file.
The run's summary page says which case applies.

## What the build does

Both routes call `Threefold.EditorTools.PrototypeBuild.BuildAndroid`, which:

1. Assembles the Broken Foundry, the Combat Arena and the Fantasy Playground from code, including
   all generated assets.
2. Validates each scene and stops if anything is missing (for example a discipline without its
   charged attack, which is how run 4 failed).
3. Sets the package ID `com.nggglobal.threefold`, the product and company names, and landscape-only rotation.
4. Builds a development APK (not an app bundle) containing those scenes. The diagnostics overlay's
   **Next scene** button moves between them.

Because the scene is assembled during the build, nothing generated has to be committed first.

## Not verified

- Route 1: Unity activation in the GameCI container with this project's licence; it has never run.
- That a built APK plays correctly: a successful build shows that Unity compiled, assembled,
  validated and packaged the game, not how it plays or performs on a phone.
- The licensing question above is unchanged; Route 2 uses Unity as building by hand on that PC would.

## Sources

- GameCI activation: https://game.ci/docs/github/activation
- GameCI builder: https://game.ci/docs/github/builder
- GameCI images: https://hub.docker.com/r/unityci/editor/tags
- Unity manual activation guide: https://docs.unity.com/en-us/engine/6000.3/manual/get-started/install-and-upgrade/licenses-and-activation/manual-license-activation/manual-activation-guide
- Unity Editor Software Terms: https://unity.com/legal/editor-terms-of-service/software
- GitHub Actions billing: https://docs.github.com/en/billing/concepts/product-billing/github-actions
- GitHub self-hosted runners: https://docs.github.com/en/actions/hosting-your-own-runners

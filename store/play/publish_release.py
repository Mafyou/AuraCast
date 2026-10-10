"""Uploads a bundle to the Play Console and puts it on a track, with the release notes kept in this folder.

    python store/play/publish_release.py <service-account.json> <bundle.aab> [--name 1.7.5] [--track production] [--status draft]

The release is created as a draft by default: it sits on the track, and sending it for review stays a deliberate
click in the console. Google only accepts drafts anyway for an app that has never been published.
Each language folder holds release-notes.txt (500 characters at most).
Needs google-api-python-client and google-auth.
"""
import sys
from pathlib import Path

from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload

PACKAGE = "fr.mafyou.auramusic"
LANGUAGES = {"fr-FR": "fr", "en-US": "en"}
NOTES_LIMIT = 500

here = Path(__file__).parent


def option(name, default):
    return sys.argv[sys.argv.index(name) + 1] if name in sys.argv else default


arguments = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith("--") and not sys.argv[i - 1].startswith("--")]
if len(arguments) != 2:
    sys.exit(__doc__)
key_file, bundle = arguments
track, status = option("--track", "production"), option("--status", "draft")

notes = []
for language, folder in LANGUAGES.items():
    text = (here / folder / "release-notes.txt").read_text(encoding="utf-8").strip()
    if len(text) > NOTES_LIMIT:
        sys.exit(f"{folder}/release-notes.txt is {len(text)} characters long, the Play Store takes {NOTES_LIMIT}.")
    notes.append({"language": language, "text": text})

credentials = service_account.Credentials.from_service_account_file(
    key_file, scopes=["https://www.googleapis.com/auth/androidpublisher"])
edits = build("androidpublisher", "v3", credentials=credentials, cache_discovery=False).edits()
edit = edits.insert(packageName=PACKAGE, body={}).execute()["id"]
try:
    uploaded = edits.bundles().upload(
        packageName=PACKAGE, editId=edit,
        media_body=MediaFileUpload(bundle, mimetype="application/octet-stream", resumable=True)).execute()
    code = uploaded["versionCode"]
    print(f"bundle uploaded: version code {code}")
    release = {"name": option("--name", Path(bundle).stem), "versionCodes": [str(code)], "status": status, "releaseNotes": notes}
    edits.tracks().update(packageName=PACKAGE, editId=edit, track=track,
                          body={"track": track, "releases": [release]}).execute()
    edits.commit(packageName=PACKAGE, editId=edit).execute()
    print(f"release {release['name']} is on the {track} track, status {status}")
except Exception:
    edits.delete(packageName=PACKAGE, editId=edit).execute()
    raise

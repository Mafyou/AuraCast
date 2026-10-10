"""Sends the Play Store listing kept in this folder to the Play Console: texts, icon, feature graphic, screenshots.

    python store/play/publish_listing.py <service-account.json> [--dry-run]

Each language folder (fr, en) holds title.txt, short-description.txt, full-description.txt, feature-graphic.png and
the numbered screenshots (NN-*.png, in that order). Whatever sits in a sub-folder (hors-fiche) is left out.
With --dry-run everything is checked and uploaded to a draft edit that is then thrown away: nothing changes.
Needs google-api-python-client and google-auth.
"""
import sys
from pathlib import Path

from google.oauth2 import service_account
from googleapiclient.discovery import build
from googleapiclient.http import MediaFileUpload

PACKAGE = "fr.mafyou.auramusic"
# Play Console language -> folder here. French of France and of Canada share the same texts.
LANGUAGES = {"fr-CA": "fr", "fr-FR": "fr", "en-US": "en"}
LIMITS = {"title": 30, "short-description": 80, "full-description": 4000}
FIELDS = {"title": "title", "short-description": "shortDescription", "full-description": "fullDescription"}

here = Path(__file__).parent
arguments = [a for a in sys.argv[1:] if not a.startswith("--")]
dry_run = "--dry-run" in sys.argv
if len(arguments) != 1:
    sys.exit(__doc__)


def texts(folder):
    listing = {}
    for name, limit in LIMITS.items():
        text = (here / folder / f"{name}.txt").read_text(encoding="utf-8").strip()
        if len(text) > limit:
            sys.exit(f"{folder}/{name}.txt is {len(text)} characters long, the Play Store takes {limit}.")
        listing[FIELDS[name]] = text
    return listing


def screenshots(folder):
    found = sorted(p for p in (here / folder).glob("[0-9][0-9]-*.png"))
    if not 2 <= len(found) <= 8:
        sys.exit(f"{folder}: {len(found)} screenshots, the Play Store wants 2 to 8.")
    return found


# Read and check everything before touching the console.
content = {folder: (texts(folder), screenshots(folder)) for folder in set(LANGUAGES.values())}

credentials = service_account.Credentials.from_service_account_file(
    arguments[0], scopes=["https://www.googleapis.com/auth/androidpublisher"])
edits = build("androidpublisher", "v3", credentials=credentials, cache_discovery=False).edits()
edit = edits.insert(packageName=PACKAGE, body={}).execute()["id"]


def replace_images(language, image_type, files):
    edits.images().deleteall(packageName=PACKAGE, editId=edit, language=language, imageType=image_type).execute()
    for file in files:
        edits.images().upload(packageName=PACKAGE, editId=edit, language=language, imageType=image_type,
                              media_body=MediaFileUpload(str(file), mimetype="image/png")).execute()


try:
    for language, folder in LANGUAGES.items():
        listing, shots = content[folder]
        edits.listings().update(packageName=PACKAGE, editId=edit, language=language,
                                body={"language": language, **listing}).execute()
        replace_images(language, "icon", [here / "icon-512.png"])
        replace_images(language, "featureGraphic", [here / folder / "feature-graphic.png"])
        replace_images(language, "phoneScreenshots", shots)
        print(f"{language}: texts, icon, feature graphic, {len(shots)} screenshots")
    if dry_run:
        edits.delete(packageName=PACKAGE, editId=edit).execute()
        print("dry run: draft edit thrown away, nothing changed")
    else:
        edits.commit(packageName=PACKAGE, editId=edit).execute()
        print("listing saved in the Play Console")
except Exception:
    edits.delete(packageName=PACKAGE, editId=edit).execute()
    raise

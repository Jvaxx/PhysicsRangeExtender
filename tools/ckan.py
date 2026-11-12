#!/usr/bin/env python3
"""Release helpers for build.sh.

  ckan.py avc-version MODDIR
      Print the mod version from MODDIR/PhysicsRangeExtender.version.
  ckan.py ckan ZIP VERSION OWNER/REPO MODDIR
      Print a CKAN metadata file (.ckan) for ZIP, downloaded from the GitHub
      release VERSION of OWNER/REPO. CKAN installs it with
      `ckan install -c FILE` / `ckan upgrade -c FILE`, or File > Install from .ckan.
"""
import datetime
import hashlib
import json
import os
import sys
import zipfile


def avc(moddir):
    with open(os.path.join(moddir, 'PhysicsRangeExtender.version')) as f:
        return json.load(f)


def triple(node):
    return '%d.%d.%d' % (node['MAJOR'], node['MINOR'], node['PATCH'])


def ckan(path, version, slug, moddir):
    with open(path, 'rb') as f:
        data = f.read()
    info = avc(moddir)
    raw = ('https://github.com/%s/raw/master/PhysicsRangeExtender/Distribution/'
           'GameData/PhysicsRangeExtender/' % slug)
    return {
        'spec_version': 'v1.18',
        'identifier': 'PhysicsRangeExtender',
        'name': 'Physics Range Extender',
        'abstract': 'Extends the load and physics range of vessels',
        'author': ['jrodrigv', 'Jvaxx'],
        'version': version,
        'ksp_version_min': triple(info['KSP_VERSION_MIN']),
        'ksp_version_max': triple(info['KSP_VERSION_MAX']),
        'license': 'Unlicense',
        'release_status': 'stable',
        'resources': {
            'repository': 'https://github.com/%s' % slug,
            'bugtracker': 'https://github.com/%s/issues' % slug,
            'remote-avc': raw + 'PhysicsRangeExtender.version',
        },
        'tags': ['plugin', 'physics'],
        'download': 'https://github.com/%s/releases/download/%s/%s'
                    % (slug, version, os.path.basename(path)),
        'download_size': len(data),
        'download_hash': {
            'sha1': hashlib.sha1(data).hexdigest().upper(),
            'sha256': hashlib.sha256(data).hexdigest().upper(),
        },
        'download_content_type': 'application/zip',
        'install_size': sum(i.file_size for i in zipfile.ZipFile(path).infolist()),
        'release_date': datetime.datetime.now(datetime.timezone.utc)
                        .strftime('%Y-%m-%dT%H:%M:%SZ'),
        'install': [{'find': 'PhysicsRangeExtender', 'install_to': 'GameData'}],
    }


def main(argv):
    if len(argv) == 3 and argv[1] == 'avc-version':
        print(triple(avc(argv[2])['VERSION']))
    elif len(argv) == 6 and argv[1] == 'ckan':
        print(json.dumps(ckan(*argv[2:6]), indent=4))
    else:
        sys.exit(__doc__)


if __name__ == '__main__':
    main(sys.argv)

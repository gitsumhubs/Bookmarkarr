#!/bin/sh
set -eu

groupadd --system bookmarkarr
useradd --system --gid bookmarkarr --home-dir /nonexistent --shell /usr/sbin/nologin --no-create-home bookmarkarr

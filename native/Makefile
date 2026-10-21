# pkmgr-box: Gen 3 style PC box viewer.
#
#   make desktop                        build a desktop binary (needs SDL2, SDL2_image, SDL2_ttf dev packages)
#   make PLATFORM=h700-nextui           build for a device, inside its NextUI toolchain container
#                                       (see .github/workflows/build.yml and scripts/build-native.sh)
#
# Device builds follow josegonzalez/minui-list: they compile against the platform layer of the
# NextUI release each device runs.

PLATFORM ?= desktop
NEXTUI_VERSION ?= v6.15.0
MY355_NEXTUI_VERSION ?= my355-latest
PARSON_VERSION ?= ec53fb6528b45811df9db0db22cab96a94a96a11
MINUI_LIST_VERSION ?= 0.15.4
MINUI_PRESENTER_VERSION ?= 0.13.4

WORKSPACE = $(patsubst %-nextui,%,$(PLATFORM))
# The Anbernic H700 devices are in NextUI itself from v6.15.0; the Miyoo Flip still has its own tag.
ifeq ($(PLATFORM),my355-nextui)
  UPSTREAM_REPO = https://github.com/LoveRetro/NextUI
  UPSTREAM_VERSION = $(MY355_NEXTUI_VERSION)
else
  UPSTREAM_REPO = https://github.com/LoveRetro/NextUI
  UPSTREAM_VERSION = $(NEXTUI_VERSION)
endif

ifeq ($(PLATFORM),desktop)
all: desktop
else
all: device
endif

include/parson:
	mkdir -p include
	git clone -q https://github.com/kgabis/parson.git include/parson
	git -C include/parson checkout -q $(PARSON_VERSION)

desktop: include/parson
	$(CC) -O2 -std=gnu99 -DDESKTOP -I. -Iinclude pkmgr-box.c include/parson/parson.c -o pkmgr-box-desktop \
		$(shell pkg-config --cflags --libs sdl2 SDL2_image SDL2_ttf) -lm

NEXTUI_DIR = nextui-$(WORKSPACE)

$(NEXTUI_DIR):
	git clone -q --depth 1 --branch $(UPSTREAM_VERSION) $(UPSTREAM_REPO) $(NEXTUI_DIR)

nextui: $(NEXTUI_DIR)

LIB_DIR = $(CURDIR)/platform/$(PLATFORM)/lib
COMMON = $(NEXTUI_DIR)/workspace/all/common
PLAT = $(NEXTUI_DIR)/workspace/$(WORKSPACE)/platform
-include $(PLAT)/makefile.env

NEXTUI_GL_LIBS = -lGLESv2 -lsamplerate
ifneq (,$(filter $(PLATFORM),tg5050-nextui my355-nextui))
  NEXTUI_GL_LIBS = -lGLESv2 -lmali -lsamplerate
endif

$(CURDIR)/platform/$(PLATFORM)/include/msettings.h: $(NEXTUI_DIR)
	mkdir -p platform/$(PLATFORM)/include platform/$(PLATFORM)/lib
	cd $(NEXTUI_DIR)/workspace/$(WORKSPACE)/libmsettings && make
	cp $(NEXTUI_DIR)/workspace/$(WORKSPACE)/libmsettings/msettings.h platform/$(PLATFORM)/include/
	cp $(NEXTUI_DIR)/workspace/$(WORKSPACE)/libmsettings/libmsettings.so platform/$(PLATFORM)/lib/

device: $(NEXTUI_DIR) include/parson $(CURDIR)/platform/$(PLATFORM)/include/msettings.h
	$(CROSS_COMPILE)gcc pkmgr-box.c include/parson/parson.c \
		$(COMMON)/scaler.c $(COMMON)/utils.c $(COMMON)/api.c $(COMMON)/config.c $(PLAT)/platform.c \
		-o pkmgr-box-$(PLATFORM) \
		$(CFLAGS) -I. -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99 \
		-L$(LIB_DIR) -L/opt/nextui/lib $(LDFLAGS) -lmsettings $(NEXTUI_GL_LIBS)

# minui-list, patched (minui-list.patch): its --font-large/--font-medium/--font-default options strncpy into
# NULL pointers and crash (still so upstream), which the per-game menu fonts need; and --options-selected-only
# shows a row's option value and image on the highlighted row only, with the image left of the value.
minui-list-src:
	git clone -q --branch $(MINUI_LIST_VERSION) https://github.com/josegonzalez/minui-list.git minui-list-src
	git -C minui-list-src apply ../minui-list.patch

minui-list: $(NEXTUI_DIR) minui-list-src include/parson $(CURDIR)/platform/$(PLATFORM)/include/msettings.h
	$(CROSS_COMPILE)gcc minui-list-src/minui-list.c $(wildcard minui-list-src/list_*.c) include/parson/parson.c \
		$(COMMON)/scaler.c $(COMMON)/utils.c $(COMMON)/api.c $(COMMON)/config.c $(PLAT)/platform.c \
		-o minui-list-$(PLATFORM) \
		$(CFLAGS) -Iminui-list-src -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99 \
		-L$(LIB_DIR) -L/opt/nextui/lib $(LDFLAGS) -lmsettings $(NEXTUI_GL_LIBS)

# minui-presenter, unchanged, but built here against the same patched NextUI (scripts/patch-nextui-keep-frame.py) so
# its message screens don't flash black either.
minui-presenter-src:
	git clone -q --branch $(MINUI_PRESENTER_VERSION) https://github.com/josegonzalez/minui-presenter.git minui-presenter-src

minui-presenter: $(NEXTUI_DIR) minui-presenter-src include/parson $(CURDIR)/platform/$(PLATFORM)/include/msettings.h
	$(CROSS_COMPILE)gcc minui-presenter-src/minui-presenter.c minui-presenter-src/presenter_theme.c include/parson/parson.c \
		$(COMMON)/scaler.c $(COMMON)/utils.c $(COMMON)/api.c $(COMMON)/config.c $(PLAT)/platform.c \
		-o minui-presenter-$(PLATFORM) \
		$(CFLAGS) -Iminui-presenter-src -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99 \
		-L$(LIB_DIR) -L/opt/nextui/lib $(LDFLAGS) -lmsettings $(NEXTUI_GL_LIBS)

# The screen host (pkmgr-host.c): one process that runs minui-list, minui-presenter and pkmgr-box, built as libraries
# (their main renamed), so the display, input and power management are opened once, not for every screen. The NextUI
# functions that open and close them are renamed REAL_* in the host's NextUI, and the host's own of those names keep
# them open; -rdynamic lets the libraries call into it. -Bsymbolic keeps each library on its own copy of what it
# defines (parson, its globals), not another's of the same name.
HOST_RENAMES = -DGFX_init=REAL_GFX_init -DGFX_quit=REAL_GFX_quit -DGFX_flip=REAL_GFX_flip \
	-DPLAT_initInput=REAL_PLAT_initInput -DPLAT_quitInput=REAL_PLAT_quitInput -DPWR_init=REAL_PWR_init -DPWR_quit=REAL_PWR_quit
HOSTED = -fPIC -shared -Dmain=pkmgr_hosted_main -Wl,-Bsymbolic

pkmgr-host: $(NEXTUI_DIR) include/parson $(CURDIR)/platform/$(PLATFORM)/include/msettings.h
	$(CROSS_COMPILE)gcc pkmgr-host.c include/parson/parson.c \
		$(COMMON)/scaler.c $(COMMON)/utils.c $(COMMON)/api.c $(COMMON)/config.c $(PLAT)/platform.c \
		-o pkmgr-host-$(PLATFORM) \
		$(CFLAGS) $(HOST_RENAMES) -I. -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99 -rdynamic \
		-L$(LIB_DIR) -L/opt/nextui/lib $(LDFLAGS) -lmsettings $(NEXTUI_GL_LIBS) -ldl

hosted: $(NEXTUI_DIR) include/parson minui-list-src minui-presenter-src $(CURDIR)/platform/$(PLATFORM)/include/msettings.h
	$(CROSS_COMPILE)gcc pkmgr-box.c include/parson/parson.c -o pkmgr-box-$(PLATFORM).so $(HOSTED) \
		$(CFLAGS) -I. -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99 -lm
	$(CROSS_COMPILE)gcc minui-list-src/minui-list.c $(wildcard minui-list-src/list_*.c) include/parson/parson.c \
		-o minui-list-$(PLATFORM).so $(HOSTED) \
		$(CFLAGS) -Iminui-list-src -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99
	$(CROSS_COMPILE)gcc minui-presenter-src/minui-presenter.c minui-presenter-src/presenter_theme.c include/parson/parson.c \
		-o minui-presenter-$(PLATFORM).so $(HOSTED) \
		$(CFLAGS) -Iminui-presenter-src -Iinclude -Iplatform/$(PLATFORM)/include -I$(COMMON) -I$(PLAT) -I/opt/nextui/include \
		-DPLATFORM=\"$(WORKSPACE)\" -DPLATFORM_NEXTUI -O2 -std=gnu99

clean:
	rm -rf pkmgr-box-* minui-list-*-nextui minui-list-trimuismart minui-list-src minui-presenter-*-nextui minui-presenter-src pkmgr-host-* *.so platform nextui-*

.PHONY: all desktop device nextui clean minui-list minui-presenter pkmgr-host hosted

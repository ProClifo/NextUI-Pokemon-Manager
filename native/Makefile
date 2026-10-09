# pkmgr-box: Gen 3 style PC box viewer.
#
#   make desktop                        build a desktop binary (needs SDL2, SDL2_image, SDL2_ttf dev packages)
#   make PLATFORM=h700-nextui           build for a device, inside its NextUI toolchain container
#                                       (see .github/workflows/build.yml and scripts/build-native.sh)
#
# Device builds follow josegonzalez/minui-list: they compile against the platform layer of the
# NextUI release each device runs.

PLATFORM ?= desktop
NEXTUI_VERSION ?= v6.14.0
H700_VERSION ?= h700-rc11
MY355_NEXTUI_VERSION ?= my355-latest
PARSON_VERSION ?= ec53fb6528b45811df9db0db22cab96a94a96a11
MINUI_LIST_VERSION ?= 0.15.4

WORKSPACE = $(patsubst %-nextui,%,$(PLATFORM))
ifeq ($(PLATFORM),h700-nextui)
  UPSTREAM_REPO = https://github.com/pvaibhav/NextUI
  UPSTREAM_VERSION = $(H700_VERSION)
else ifeq ($(PLATFORM),my355-nextui)
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

clean:
	rm -rf pkmgr-box-* minui-list-*-nextui minui-list-src platform nextui-*

.PHONY: all desktop device nextui clean minui-list

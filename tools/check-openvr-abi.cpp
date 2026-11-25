// Compile against the same Valve SDK headers used by generate-openvr-slots.py:
// c++ -std=c++17 -I /path/to/openvr/headers tools/check-openvr-abi.cpp -o /tmp/openvr-abi
// No runtime connection or native library is needed.
#include <cstddef>
#include <openvr.h>
#include <openvr_capi.h>
static_assert(sizeof(void*) == 8);
static_assert(sizeof(HmdMatrix34_t) == 48);
static_assert(sizeof(TrackedDevicePose_t) == 80);
static_assert(sizeof(InputDigitalActionData_t) == 24);
static_assert(sizeof(InputAnalogActionData_t) == 48);
static_assert(sizeof(InputPoseActionData_t) == 96);
static_assert(sizeof(VRActiveActionSet_t) == 32);
static_assert(sizeof(PathRead_t) == 40);
static_assert(sizeof(PathWrite_t) == 56);
static_assert(offsetof(InputDigitalActionData_t, activeOrigin) == 8);
static_assert(offsetof(PathWrite_t, pszPath) == 40);
#ifdef _WIN32
static_assert(sizeof(vr::VREvent_t) == 64);
#else
static_assert(sizeof(vr::VREvent_t) == 60);
#endif
int main() {}

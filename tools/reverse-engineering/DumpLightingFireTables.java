// Dump the fixed tables consumed by CFireEffect.
// @category OpenSynapse

import ghidra.app.script.GhidraScript;
import ghidra.program.model.address.Address;

public class DumpLightingFireTables extends GhidraScript {
    @Override
    public void run() throws Exception {
        for (int row = 0; row < 7; row++) {
            dump("propagation-row-" + row, 0x1f5333 - row * 23, 23);
        }
        dump("source-mask", 0x1f534a, 23);
        for (int row = 0; row < 7; row++) {
            dump("color-row-" + row, 0x1f5370 + row * 23, 23);
        }
    }

    private void dump(String name, long rva, int length) throws Exception {
        Address address = currentProgram.getImageBase().add(rva);
        byte[] bytes = new byte[length];
        currentProgram.getMemory().getBytes(address, bytes);
        StringBuilder hex = new StringBuilder(length * 2);
        for (byte value : bytes) {
            hex.append(String.format("%02X", value & 0xff));
        }
        println(name + " RVA 0x" + Long.toHexString(rva) + " " + hex);
    }
}

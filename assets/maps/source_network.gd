extends RefCounted

static func is_client(node: Node) -> bool:
	var session := node.get_node_or_null("/root/LanSession")
	return session != null and bool(session.get("IsClient"))

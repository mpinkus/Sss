db = db.getSiblingDB('shamir_ceremony');

db.createUser({
  user: process.env.MONGO_CEREMONY_USER || 'ceremony_user',
  pwd: process.env.MONGO_CEREMONY_PASSWORD || 'ceremony_password',
  roles: [
    {
      role: 'readWrite',
      db: 'shamir_ceremony'
    }
  ]
});

db.createCollection('ceremonies');
db.createCollection('sessions');

print('MongoDB initialization completed');
